namespace Codec.Api.Models;

public enum AdminActionType
{
    UserDisabled,
    UserEnabled,
    UserGlobalBanned,
    UserForcedLogout,
    UserPasswordReset,
    UserPromotedAdmin,
    UserDemotedAdmin,
    ServerQuarantined,
    ServerUnquarantined,
    ServerDeleted,
    ServerOwnershipTransferred,
    ReportResolved,
    ReportDismissed,
    AnnouncementCreated,
    AnnouncementDeleted,
    MessagesPurged
}
