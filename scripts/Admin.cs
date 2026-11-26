using System.Collections.Generic;

namespace TwinRealms;

/// <summary>
/// Admin flag. False by default (offline too: a solo player is still protected in towns). F-43 (real admin accounts) sets
/// IsAdmin for this machine after login and AdminPeers on the host for each logged-in admin peer. Admins bypass town zones (TownZones).
/// </summary>
public static class Admin
{
    /// <summary>This machine's player is an admin (hook for F-43; DevCapture sets it for tests).</summary>
    public static bool IsAdmin;
    /// <summary>Host side: multiplayer peer ids that are admins (hook for F-43).</summary>
    public static readonly HashSet<long> AdminPeers = new();
    public static bool IsPeerAdmin(long peer) => AdminPeers.Contains(peer);
}
