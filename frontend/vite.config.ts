import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Cổng 5173 phải khớp redirectUris/webOrigins của client marketplace-web trong Keycloak
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true, host: true },
});
