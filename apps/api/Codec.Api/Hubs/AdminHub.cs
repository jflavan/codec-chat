using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Codec.Api.Hubs;

[Authorize(Policy = "GlobalAdmin")]
public class AdminHub : Hub;
