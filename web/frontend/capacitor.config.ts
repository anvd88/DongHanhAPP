import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  appId: "com.ketoanmini.hr",
  appName: "KetoanMini HR",
  webDir: ".android-wwwroot",
  server: {
    androidScheme: "https",
    allowNavigation: ["192.168.1.88"],
  },
};

export default config;
