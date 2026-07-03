import { useRef, useState } from "react";
import { Save, KeyRound, Check, Camera, Crop } from "lucide-react";
import { Modal } from "./Modal";
import { AvatarCropper } from "./AvatarCropper";
import { Button, Input, Field } from "./ui";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { initials } from "../lib/format";
import type { User } from "../lib/types";

/** Sửa hồ sơ của chính mình — đổi tên hiển thị + ảnh đại diện (đồng nhất với dialog "Tùy chỉnh tài khoản" trên desktop). */
export function EditProfileModal({ onClose }: { onClose: () => void }) {
  const { user, updateUser } = useAuth();
  const [fullName, setFullName] = useState(user?.fullName ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  // undefined = giữ nguyên ảnh hiện tại, null = xóa ảnh, string = ảnh mới (data URL).
  const [avatarChange, setAvatarChange] = useState<string | null | undefined>(undefined);
  const [cropSrc, setCropSrc] = useState<string | null>(null); // object URL của ảnh đang chỉnh
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const preview = avatarChange === undefined ? user?.avatarUrl ?? null : avatarChange;

  const pickFile = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = ""; // cho phép chọn lại cùng một tệp
    if (!file) return;
    if (!file.type.startsWith("image/")) {
      setError("Vui lòng chọn một tệp ảnh.");
      return;
    }
    setError("");
    setCropSrc(URL.createObjectURL(file)); // mở khung chỉnh
  };

  const closeCrop = () => {
    if (cropSrc && cropSrc.startsWith("blob:")) URL.revokeObjectURL(cropSrc);
    setCropSrc(null);
  };

  const save = async () => {
    if (!fullName.trim()) {
      setError("Vui lòng nhập tên hiển thị.");
      return;
    }
    setSaving(true);
    setError("");
    try {
      await api.put<User>("/api/auth/profile", { fullName: fullName.trim(), email: email.trim() });
      if (avatarChange !== undefined) {
        if (avatarChange) await api.put<User>("/api/auth/avatar", { imageDataUrl: avatarChange });
        else await api.del<User>("/api/auth/avatar");
      }
      const updated = await api.get<User>("/api/auth/me");
      updateUser(updated);
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi");
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
    <Modal
      open
      panel
      onClose={onClose}
      title="Sửa hồ sơ"
      className="profile-modal-strong"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>Hủy</Button>
          <Button onClick={save} loading={saving}><Save className="h-4 w-4" />Lưu</Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="flex items-center gap-4">
          <div className="h-20 w-20 shrink-0 overflow-hidden rounded-full" style={{ border: "1px solid var(--glass-border)" }}>
            {preview ? (
              <img src={preview} alt="Ảnh đại diện" className="h-full w-full object-cover" />
            ) : (
              <div
                className="grid h-full w-full place-items-center text-2xl font-bold text-white"
                style={{ background: "linear-gradient(135deg, var(--accent), var(--purple))" }}
              >
                {initials(user?.fullName || user?.username || "?")}
              </div>
            )}
          </div>
          <div className="flex flex-col items-start gap-2">
            <input ref={fileRef} type="file" accept="image/*" className="hidden" onChange={pickFile} />
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="ghost" onClick={() => fileRef.current?.click()}>
                <Camera className="h-4 w-4" />
                Chọn ảnh
              </Button>
              {preview && (
                <Button variant="ghost" onClick={() => setCropSrc(preview)}>
                  <Crop className="h-4 w-4" />
                  Chỉnh khung
                </Button>
              )}
            </div>
          </div>
        </div>
        <Field label="Tên đăng nhập">
          <Input value={user?.username ?? ""} disabled className="opacity-70" />
        </Field>
        <Field label="Tên hiển thị *">
          <Input
            value={fullName}
            autoFocus
            onChange={(e) => setFullName(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && save()}
          />
        </Field>
        <Field label="Email">
          <Input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && save()}
          />
        </Field>
        {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
      </div>
    </Modal>
    {cropSrc && (
      <AvatarCropper
        src={cropSrc}
        onCancel={closeCrop}
        onDone={(dataUrl) => {
          setAvatarChange(dataUrl);
          closeCrop();
        }}
        onDelete={() => {
          setAvatarChange(null);
          closeCrop();
        }}
      />
    )}
    </>
  );
}

/** Đổi mật khẩu của chính mình — nhập mật khẩu hiện tại + mật khẩu mới (xác minh ở backend). */
export function ChangePasswordModal({ onClose }: { onClose: () => void }) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [done, setDone] = useState(false);
  const [saving, setSaving] = useState(false);

  const save = async () => {
    if (!next.trim()) {
      setError("Vui lòng nhập mật khẩu mới.");
      return;
    }
    if (next !== confirm) {
      setError("Mật khẩu nhập lại không khớp.");
      return;
    }
    setSaving(true);
    setError("");
    try {
      await api.post("/api/auth/change-password", { currentPassword: current, newPassword: next });
      setDone(true);
      setTimeout(onClose, 1200);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Lỗi");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Đổi mật khẩu"
      footer={
        done ? (
          <Button variant="ghost" onClick={onClose}>Đóng</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={onClose}>Hủy</Button>
            <Button onClick={save} loading={saving}><KeyRound className="h-4 w-4" />Cập nhật</Button>
          </>
        )
      }
    >
      {done ? (
        <div className="flex flex-col items-center gap-2 py-6 text-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-emerald-500/15 text-emerald-600">
            <Check className="h-6 w-6" />
          </div>
          <p className="text-sm font-semibold text-[var(--text)]">Đã đổi mật khẩu thành công.</p>
        </div>
      ) : (
        <div className="space-y-4">
          <Field label="Mật khẩu hiện tại *">
            <Input type="password" value={current} autoFocus onChange={(e) => setCurrent(e.target.value)} />
          </Field>
          <Field label="Mật khẩu mới *">
            <Input type="password" value={next} onChange={(e) => setNext(e.target.value)} />
          </Field>
          <Field label="Nhập lại mật khẩu mới *">
            <Input
              type="password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && save()}
            />
          </Field>
          {error && <div className="rounded-xl bg-red-500/10 px-3 py-2.5 text-sm font-medium text-[var(--danger)]">{error}</div>}
        </div>
      )}
    </Modal>
  );
}
