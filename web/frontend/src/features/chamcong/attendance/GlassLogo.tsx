import { FaceScanIcon } from "./FaceScanIcon";

/** Logo kính vuông bo góc chứa icon nhận diện khuôn mặt (góc trái header). */
export function GlassLogo() {
  return (
    <span className="att-logo" aria-hidden="true">
      <FaceScanIcon className="att-logo-icon" />
    </span>
  );
}
