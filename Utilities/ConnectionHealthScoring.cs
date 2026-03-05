using System;
using System.Collections.Generic;

namespace Zer0Talk.Utilities
{
    public static class ConnectionHealthScoring
    {
        public static Result Evaluate(NetworkDiagnostics.Snapshot snap)
        {
            var directTotal = snap.DirectSuccess + snap.DirectFail;
            var natTotal = snap.NatSuccess + snap.NatFail;
            var relayTotal = snap.RelaySuccess + snap.RelayFail;

            var score = 100;
            if (snap.UidMismatch > 0) score -= Math.Min(25, (int)Math.Min(int.MaxValue, snap.UidMismatch * 5));
            if (directTotal > 0 && snap.DirectSuccess == 0) score -= 25;
            if (natTotal > 0 && snap.NatSuccess == 0) score -= 20;
            if (relayTotal > 0 && snap.RelaySuccess == 0) score -= 15;
            score = Math.Clamp(score, 0, 100);

            var doctorTips = new List<string>();
            if (snap.UidMismatch > 0) doctorTips.Add("Identity mismatch seen: re-verify contact key and reconnect.");
            if (natTotal > 0 && snap.NatSuccess == 0) doctorTips.Add("NAT path failing: rerun mapping verification and check router UPnP.");
            if (directTotal > 0 && snap.DirectSuccess == 0) doctorTips.Add("Direct path unavailable: verify local firewall and listening port.");
            if (relayTotal > 0 && snap.RelaySuccess == 0) doctorTips.Add("Relay failing: switch relay endpoint or retry later.");
            if (doctorTips.Count == 0) doctorTips.Add("Network looks healthy. No action needed.");

            return new Result(score, string.Join(" ", doctorTips));
        }

        public readonly record struct Result(int Score, string DoctorSummary);
    }
}