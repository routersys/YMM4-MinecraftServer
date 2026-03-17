namespace MinecraftHost.Models.Authorization;

public enum OperationType
{
    CreateServer,
    DeleteServer,
    StartServer,
    StopServer,
    UpdateServerBuild,
    ManagePlugins,
    ManageMods,
    EditServerFiles,
    SendConsoleCommand,
    ViewAuditLog,
    ViewOperationsCenter,
    ManagePolicy
}