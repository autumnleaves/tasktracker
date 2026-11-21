import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In development, run the API too (`dotnet run --project src/TaskTracker.Api`, port 5280):
// the dev server forwards /api to it, as the API does for the built app in production.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:5280" },
  },
});
