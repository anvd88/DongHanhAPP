import type { CapacitorConfig } from "@capacitor/cli";

const config: CapacitorConfig = {
  appId: "com.ketoanmini.hr",
  appName: "Ketoan - Nhân sự",
  webDir: ".android-wwwroot",
  server: {
    androidScheme: "https",
    allowNavigation: ["app.ketoancp.click", "ketoancp.click", "192.168.1.88"],
  },
};

export default config;
