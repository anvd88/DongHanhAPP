use http::HeaderMap;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

/// Trust proxy headers only when the direct peer is loopback. This matches the current deployment,
/// where cloudflared/front proxy runs on the same host, and prevents LAN clients spoofing their IP.
pub fn client_ip(peer_ip: IpAddr, headers: &HeaderMap) -> IpAddr {
    if !peer_ip.is_loopback() {
        return peer_ip;
    }

    headers
        .get("x-forwarded-for")
        .and_then(|value| value.to_str().ok())
        .and_then(|value| value.split(',').next())
        .and_then(|value| value.trim().parse().ok())
        .unwrap_or(peer_ip)
}

pub fn forwarded_for(peer_ip: IpAddr, headers: &HeaderMap) -> String {
    if peer_ip.is_loopback()
        && let Some(valid) = headers
            .get("x-forwarded-for")
            .and_then(|value| value.to_str().ok())
            .filter(|value| forwarded_chain_is_valid(value))
    {
        return valid.to_owned();
    }
    peer_ip.to_string()
}

pub fn forwarded_proto(peer_ip: IpAddr, headers: &HeaderMap) -> &'static str {
    if peer_ip.is_loopback()
        && let Some(value) = headers
            .get("x-forwarded-proto")
            .and_then(|value| value.to_str().ok())
    {
        if value.eq_ignore_ascii_case("https") {
            return "https";
        }
        if value.eq_ignore_ascii_case("http") {
            return "http";
        }
    }
    "http"
}

fn forwarded_chain_is_valid(value: &str) -> bool {
    let mut count = 0;
    for item in value.split(',') {
        count += 1;
        if count > 16 || item.trim().parse::<IpAddr>().is_err() {
            return false;
        }
    }
    count > 0
}

pub fn is_internal(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(ip) => is_internal_v4(ip),
        IpAddr::V6(ip) => ip
            .to_ipv4_mapped()
            .map(is_internal_v4)
            .unwrap_or_else(|| is_internal_v6(ip)),
    }
}

fn is_internal_v4(ip: Ipv4Addr) -> bool {
    let [a, b, _, _] = ip.octets();
    ip.is_loopback()
        || a == 10
        || (a == 172 && (16..=31).contains(&b))
        || (a == 192 && b == 168)
        || (a == 169 && b == 254)
}

fn is_internal_v6(ip: Ipv6Addr) -> bool {
    let [a, b, ..] = ip.octets();
    ip.is_loopback() || (a & 0xfe) == 0xfc || (a == 0xfe && (b & 0xc0) == 0x80)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn matches_existing_internal_network_ranges() {
        for ip in [
            "127.0.0.1",
            "10.2.3.4",
            "172.16.0.1",
            "172.31.255.254",
            "192.168.1.88",
            "169.254.2.3",
            "::1",
            "fd12::1",
            "fe80::1",
            "::ffff:192.168.1.88",
        ] {
            assert!(is_internal(ip.parse().unwrap()), "{ip} should be internal");
        }

        for ip in ["8.8.8.8", "172.15.0.1", "172.32.0.1", "203.0.113.1"] {
            assert!(!is_internal(ip.parse().unwrap()), "{ip} should be public");
        }
    }

    #[test]
    fn ignores_forwarded_headers_from_non_loopback_peers() {
        let mut headers = HeaderMap::new();
        headers.insert("x-forwarded-for", "203.0.113.9".parse().unwrap());
        assert_eq!(
            client_ip("192.168.1.25".parse().unwrap(), &headers),
            "192.168.1.25".parse::<IpAddr>().unwrap()
        );
    }
}
