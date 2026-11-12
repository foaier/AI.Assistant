export function usePermissions() {
  return { hasPermission: (_permission: string) => true, isSuperAdmin: true };
}
