import { defineConfig } from "@neon/config/v1";

export default defineConfig({
  preview: {
    realtime: true,
  },
  auth: true,
  functions: {
    backup: { name: "Sol backup API", source: "./cloud/backup.ts" },
  },
  buckets: {
    backups: { access: "private" },
  },
  triggers: {
    "backup-cleanup": {
      type: "schedule", function: "backup", cron: "*/15 * * * *", functionPath: "/internal/cleanup",
    },
  },
  // Branch policy: per-branch tuning
  branch: (branch) => {
    if (branch.isDefault) {
      // Default branch: no overrides, uses project defaults
      return {};
    }
    if (!branch.exists) {
      // New non-default branches: auto-expire
      // Run `neon checkout <name>` to create a new branch with these settings
      return { ttl: "7d" };
    }
    // Existing branch: no changes
    return {};
  },
});
