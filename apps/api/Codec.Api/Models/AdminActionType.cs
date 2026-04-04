using System.Text.Json.Serialization;

namespace Codec.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
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
    AnnouncementUpdated,
    AnnouncementDeleted,
    MessagesPurged
}
