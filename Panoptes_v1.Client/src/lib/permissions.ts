// Ίδια ονόματα με το Constants/Permissions.cs του backend
export const Permissions = {
  Users: {
    View: 'Permissions.Users.View',
    Create: 'Permissions.Users.Create',
    Edit: 'Permissions.Users.Edit',
    Delete: 'Permissions.Users.Delete',
  },
  Roles: {
    View: 'Permissions.Roles.View',
    Create: 'Permissions.Roles.Create',
    Edit: 'Permissions.Roles.Edit',
    Delete: 'Permissions.Roles.Delete',
  },
  AuditLogs: {
    View: 'Permissions.AuditLogs.View',
  },
  Media: {
    View: 'Permissions.Media.View',
    Upload: 'Permissions.Media.Upload',
    Edit: 'Permissions.Media.Edit',
    Delete: 'Permissions.Media.Delete',
    ManageAll: 'Permissions.Media.ManageAll',
  },
  Settings: {
    View: 'Permissions.Settings.View',
    Edit: 'Permissions.Settings.Edit',
  },
  Clients: {
    View: 'Permissions.Clients.View',
    Create: 'Permissions.Clients.Create',
    Edit: 'Permissions.Clients.Edit',
    Archive: 'Permissions.Clients.Archive',
  },
  Finance: {
    Settings: 'Permissions.Finance.Settings',
  },
} as const
