using System;
using System.Text.Json;
using BlueSky.Networking;

namespace BlueSky.Tests;

public static class NetworkingTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running EOS Networking & Snapshot Replication Tests...");
        bool passed = true;
        
        passed &= TestNetworkedEntityStateSerialization();
        
        return passed;
    }

    private static bool TestNetworkedEntityStateSerialization()
    {
        try
        {
            var state = new NetworkedEntityState
            {
                EntityId = 42,
                PlayerId = "player_1",
                Position = new float[] { 10f, 20f, 30f },
                Rotation = new float[] { 0f, 0f, 0f, 1f },
                Velocity = new float[] { 1f, 0f, 0f }
            };

            string json = JsonSerializer.Serialize(state);
            var deserialized = JsonSerializer.Deserialize<NetworkedEntityState>(json);

            bool valid = deserialized != null && deserialized.EntityId == 42 && deserialized.Position[0] == 10f;
            Console.WriteLine($"  ✓ Network State JSON Serialization (EntityId={deserialized?.EntityId}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Network Replication Exception: {ex.Message}");
            return false;
        }
    }
}

