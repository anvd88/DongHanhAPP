//! Password hashing compatible with the existing .NET backend.
//!
//! New values use the application's custom Argon2id envelope rather than the
//! usual PHC string:
//! `ARGON2ID$v=19$m=19456,t=2,p=1$<base64 salt>$<base64 hash>`.
//! Legacy `PBKDF2$<iterations>$<base64 salt>$<base64 hash>` values remain
//! readable so a successful login can migrate them without resetting a user's
//! password.

use argon2::{Algorithm, Argon2, Params, Version};
use base64::{Engine as _, engine::general_purpose::STANDARD};
use pbkdf2::{pbkdf2_hmac, sha2::Sha256};
use subtle::ConstantTimeEq;

const ARGON2_PREFIX: &str = "ARGON2ID";
const PBKDF2_PREFIX: &str = "PBKDF2";
const ARGON2_VERSION: u32 = 19;
const ARGON2_MEMORY_KIB: u32 = 19_456;
const ARGON2_ITERATIONS: u32 = 2;
const ARGON2_PARALLELISM: u32 = 1;
const SALT_SIZE: usize = 16;
const KEY_SIZE: usize = 32;

// Stored hashes are database data, but parsing them must still be bounded: a
// corrupt row must not be able to request unbounded RAM or CPU from a login
// worker. These ceilings comfortably include the current policy and plausible
// future upgrades. Raise them together with a deliberate policy migration.
const MAX_VERIFY_MEMORY_KIB: u32 = 256 * 1024;
const MAX_VERIFY_ITERATIONS: u32 = 10;
const MAX_VERIFY_PARALLELISM: u32 = 16;
const MAX_PBKDF2_ITERATIONS: u32 = 10_000_000;

/// Result of a password check. Callers must only replace a stored hash when
/// `verified` and `needs_rehash` are both true.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[must_use]
pub struct VerifyResult {
    pub verified: bool,
    pub needs_rehash: bool,
}

impl VerifyResult {
    const fn invalid(needs_rehash: bool) -> Self {
        Self {
            verified: false,
            needs_rehash,
        }
    }
}

/// Hash creation failed without exposing password, salt, or backend details.
#[derive(Debug, thiserror::Error)]
pub enum PasswordHashError {
    #[error("the operating-system random number generator is unavailable")]
    RandomUnavailable,
    #[error("Argon2id rejected the password hashing parameters")]
    Argon2,
}

/// Create a new Argon2id hash using a 128-bit salt from the operating system's
/// cryptographically secure random number generator.
pub fn hash(password: &str) -> Result<String, PasswordHashError> {
    let mut salt = [0_u8; SALT_SIZE];
    getrandom::fill(&mut salt).map_err(|_| PasswordHashError::RandomUnavailable)?;
    encode_argon2(
        password,
        &salt,
        ARGON2_MEMORY_KIB,
        ARGON2_ITERATIONS,
        ARGON2_PARALLELISM,
    )
}

/// Verify either the current Argon2id format or the legacy PBKDF2-SHA256
/// format. Unsupported and malformed values fail closed.
pub fn verify(password: &str, stored_hash: &str) -> VerifyResult {
    if stored_hash.starts_with(concat!("ARGON2ID", "$")) {
        let Some(parsed) = parse_argon2(stored_hash) else {
            return VerifyResult::invalid(true);
        };
        let rehash = parsed.needs_rehash();
        return VerifyResult {
            verified: verify_argon2(password, &parsed),
            needs_rehash: rehash,
        };
    }

    if stored_hash.starts_with(concat!("PBKDF2", "$")) {
        return VerifyResult {
            verified: verify_pbkdf2(password, stored_hash),
            needs_rehash: true,
        };
    }

    VerifyResult::invalid(true)
}

/// Match the .NET migration policy: PBKDF2/unknown/malformed values need an
/// upgrade, as do Argon2id values weaker than the current policy or using a
/// different lane count.
pub fn needs_rehash(stored_hash: &str) -> bool {
    if !stored_hash.starts_with(concat!("ARGON2ID", "$")) {
        return true;
    }

    parse_argon2(stored_hash).is_none_or(|parsed| parsed.needs_rehash())
}

fn encode_argon2(
    password: &str,
    salt: &[u8],
    memory_kib: u32,
    iterations: u32,
    parallelism: u32,
) -> Result<String, PasswordHashError> {
    let hash = compute_argon2(password, salt, memory_kib, iterations, parallelism)?;
    Ok(format!(
        "{ARGON2_PREFIX}$v={ARGON2_VERSION}$m={memory_kib},t={iterations},p={parallelism}${}${}",
        STANDARD.encode(salt),
        STANDARD.encode(hash)
    ))
}

fn compute_argon2(
    password: &str,
    salt: &[u8],
    memory_kib: u32,
    iterations: u32,
    parallelism: u32,
) -> Result<[u8; KEY_SIZE], PasswordHashError> {
    let params = Params::new(memory_kib, iterations, parallelism, Some(KEY_SIZE))
        .map_err(|_| PasswordHashError::Argon2)?;
    let argon2 = Argon2::new(Algorithm::Argon2id, Version::V0x13, params);
    let mut output = [0_u8; KEY_SIZE];
    argon2
        .hash_password_into(password.as_bytes(), salt, &mut output)
        .map_err(|_| PasswordHashError::Argon2)?;
    Ok(output)
}

struct ParsedArgon2<'a> {
    salt_base64: &'a str,
    expected: Vec<u8>,
    memory_kib: u32,
    iterations: u32,
    parallelism: u32,
}

impl ParsedArgon2<'_> {
    fn needs_rehash(&self) -> bool {
        self.memory_kib < ARGON2_MEMORY_KIB
            || self.iterations < ARGON2_ITERATIONS
            || self.parallelism != ARGON2_PARALLELISM
    }

    fn is_within_verification_limits(&self) -> bool {
        self.memory_kib <= MAX_VERIFY_MEMORY_KIB
            && self.iterations <= MAX_VERIFY_ITERATIONS
            && self.parallelism <= MAX_VERIFY_PARALLELISM
    }
}

/// Parse exactly five envelope fields. As in the .NET implementation, the
/// version field is retained for wire compatibility but Argon2 v=19 is used to
/// calculate the key; the m/t/p values are self-describing.
fn parse_argon2(stored_hash: &str) -> Option<ParsedArgon2<'_>> {
    let mut parts = stored_hash.split('$');
    let prefix = parts.next()?;
    let _version = parts.next()?;
    let parameter_text = parts.next()?;
    let salt_base64 = parts.next()?;
    let hash_base64 = parts.next()?;
    if parts.next().is_some() || prefix != ARGON2_PREFIX {
        return None;
    }

    let mut memory_kib = 0_i32;
    let mut iterations = 0_i32;
    let mut parallelism = 0_i32;
    for item in parameter_text.split(',') {
        let (key, value) = item.split_once('=')?;
        // Int32.TryParse in .NET accepts surrounding whitespace and a sign.
        let value = value.trim().parse::<i32>().ok()?;
        match key {
            "m" => memory_kib = value,
            "t" => iterations = value,
            "p" => parallelism = value,
            _ => {}
        }
    }
    if memory_kib <= 0 || iterations <= 0 || parallelism <= 0 {
        return None;
    }

    Some(ParsedArgon2 {
        salt_base64,
        expected: decode_dotnet_base64(hash_base64)?,
        memory_kib: memory_kib as u32,
        iterations: iterations as u32,
        parallelism: parallelism as u32,
    })
}

fn verify_argon2(password: &str, parsed: &ParsedArgon2<'_>) -> bool {
    if parsed.expected.len() != KEY_SIZE || !parsed.is_within_verification_limits() {
        return false;
    }
    let Some(salt) = decode_dotnet_base64(parsed.salt_base64) else {
        return false;
    };
    let Ok(actual) = compute_argon2(
        password,
        &salt,
        parsed.memory_kib,
        parsed.iterations,
        parsed.parallelism,
    ) else {
        return false;
    };

    bool::from(actual.as_slice().ct_eq(parsed.expected.as_slice()))
}

fn verify_pbkdf2(password: &str, stored_hash: &str) -> bool {
    let mut parts = stored_hash.split('$');
    let Some(prefix) = parts.next() else {
        return false;
    };
    let Some(iteration_text) = parts.next() else {
        return false;
    };
    let Some(salt_base64) = parts.next() else {
        return false;
    };
    let Some(hash_base64) = parts.next() else {
        return false;
    };
    if parts.next().is_some() || prefix != PBKDF2_PREFIX {
        return false;
    }

    let Ok(iterations) = iteration_text.trim().parse::<i32>() else {
        return false;
    };
    if iterations <= 0 || iterations as u32 > MAX_PBKDF2_ITERATIONS {
        return false;
    }
    let Some(salt) = decode_dotnet_base64(salt_base64) else {
        return false;
    };
    let Some(expected) = decode_dotnet_base64(hash_base64) else {
        return false;
    };

    // Every legacy producer in this system used a 128-bit salt and 256-bit
    // derived key. Reject truncated/empty values rather than authenticating a
    // malformed database row with a weakened comparison.
    if salt.len() != SALT_SIZE || expected.len() != KEY_SIZE {
        return false;
    }

    let mut actual = [0_u8; KEY_SIZE];
    pbkdf2_hmac::<Sha256>(password.as_bytes(), &salt, iterations as u32, &mut actual);
    bool::from(actual.as_slice().ct_eq(expected.as_slice()))
}

/// `Convert.FromBase64String` ignores whitespace. Preserve that behavior while
/// using the canonical padded standard alphabet emitted by .NET.
fn decode_dotnet_base64(value: &str) -> Option<Vec<u8>> {
    if value.chars().any(char::is_whitespace) {
        let compact: String = value.chars().filter(|ch| !ch.is_whitespace()).collect();
        STANDARD.decode(compact.as_bytes()).ok()
    } else {
        STANDARD.decode(value.as_bytes()).ok()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    // Generated independently with the production C# dependency
    // Konscious.Security.Cryptography.Argon2 1.3.1 and salt bytes 0x00..0x0f.
    const DOTNET_ARGON2_FIXTURE: &str = concat!(
        "ARGON2ID$v=19$m=19456,t=2,p=1$",
        "AAECAwQFBgcICQoLDA0ODw==$",
        "UwHmtKGGzliXklU7wPAHNkZgRdbezC5K7f0JDxbtfj8="
    );

    // Generated independently with .NET 8 Rfc2898DeriveBytes.Pbkdf2,
    // HMAC-SHA256, 100,000 iterations, and salt bytes 0x10..0x1f.
    const DOTNET_PBKDF2_FIXTURE: &str = concat!(
        "PBKDF2$100000$EBESExQVFhcYGRobHB0eHw==$",
        "Me6uxmcel/V10TFfQbs/A5Yp+sNGmci8tvMSoSxgoUo="
    );

    #[test]
    fn verifies_dotnet_argon2id_fixture_byte_for_byte() {
        let salt: [u8; SALT_SIZE] = core::array::from_fn(|index| index as u8);
        let encoded = encode_argon2(
            "Mật-khẩu-độc-lập!🔐",
            &salt,
            ARGON2_MEMORY_KIB,
            ARGON2_ITERATIONS,
            ARGON2_PARALLELISM,
        )
        .expect("fixture parameters");
        assert_eq!(encoded, DOTNET_ARGON2_FIXTURE);

        assert_eq!(
            verify("Mật-khẩu-độc-lập!🔐", DOTNET_ARGON2_FIXTURE),
            VerifyResult {
                verified: true,
                needs_rehash: false,
            }
        );
        assert!(!verify("sai-mật-khẩu", DOTNET_ARGON2_FIXTURE).verified);
    }

    #[test]
    fn verifies_dotnet_legacy_pbkdf2_and_requests_migration() {
        assert_eq!(
            verify("Mật-khẩu-PBKDF2!🔐", DOTNET_PBKDF2_FIXTURE),
            VerifyResult {
                verified: true,
                needs_rehash: true,
            }
        );
        assert!(!verify("khac", DOTNET_PBKDF2_FIXTURE).verified);
    }

    #[test]
    fn new_hashes_round_trip_use_policy_and_have_unique_csprng_salts() {
        let first = hash("m4t-kh4u!").expect("hash");
        let second = hash("m4t-kh4u!").expect("hash");

        assert!(first.starts_with("ARGON2ID$v=19$m=19456,t=2,p=1$"));
        assert_ne!(first, second);
        assert!(verify("m4t-kh4u!", &first).verified);
        assert!(!verify("sai-mat-khau", &first).verified);
        assert!(!needs_rehash(&first));
    }

    #[test]
    fn rehash_policy_matches_dotnet_rules() {
        assert!(needs_rehash(DOTNET_PBKDF2_FIXTURE));
        assert!(needs_rehash(
            &DOTNET_ARGON2_FIXTURE.replace("m=19456", "m=19455")
        ));
        assert!(needs_rehash(&DOTNET_ARGON2_FIXTURE.replace("t=2", "t=1")));
        assert!(needs_rehash(&DOTNET_ARGON2_FIXTURE.replace("p=1", "p=2")));
        assert!(!needs_rehash(
            &DOTNET_ARGON2_FIXTURE.replace("m=19456", "m=19457")
        ));
    }

    #[test]
    fn malformed_and_unknown_hashes_fail_closed_without_panicking() {
        for stored in [
            "",
            "khong-phai-dinh-dang-hop-le",
            "argon2id$v=19$m=19456,t=2,p=1$AA==$AA==",
            "ARGON2ID$v=19$m=0,t=2,p=1$AA==$AA==",
            "ARGON2ID$v=19$m=19456,t=2$AA==$AA==",
            "ARGON2ID$v=19$m=19456,t=2,p=1$AA==$khong-base64",
            "PBKDF2$0$EBESExQVFhcYGRobHB0eHw==$AA==",
            "PBKDF2$100000$$",
        ] {
            let result = verify("bat-ky", stored);
            assert!(!result.verified, "unexpectedly accepted {stored}");
        }
    }

    #[test]
    fn dotnet_base64_whitespace_compatibility_is_preserved() {
        let with_whitespace = DOTNET_PBKDF2_FIXTURE
            .replace("EBESExQVFhcYGRobHB0eHw==", "EBESExQVFhcY\r\nGRobHB0eHw==");
        assert!(verify("Mật-khẩu-PBKDF2!🔐", &with_whitespace).verified);
    }

    #[test]
    fn excessive_work_factors_are_rejected_before_computation() {
        let excessive_argon = DOTNET_ARGON2_FIXTURE.replace("m=19456", "m=262145");
        assert!(!verify("Mật-khẩu-độc-lập!🔐", &excessive_argon).verified);

        let excessive_pbkdf2 = DOTNET_PBKDF2_FIXTURE.replace("100000", "10000001");
        assert!(!verify("Mật-khẩu-PBKDF2!🔐", &excessive_pbkdf2).verified);
    }
}
