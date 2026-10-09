using System;
using System.Numerics;
using BlueSky.Audio;
using BlueSky.AI.Overthinking;

namespace BlueSky.Tests;

public static class AudioAITests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Echo Audio & Overthinking AI Tests...");
        bool passed = true;
        
        passed &= TestSpatialAudioAttenuation();
        passed &= TestOverthinkingAIBrain();
        
        return passed;
    }

    private static bool TestSpatialAudioAttenuation()
    {
        try
        {
            var audio = new SpatialAudio();
            audio.UpdateListener(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero);
            
            var source = new AudioSource
            {
                Is3D = true,
                Position = new Vector3(0, 0, 10),
                MinDistance = 1.0f,
                MaxDistance = 100.0f,
                Volume = 1.0f
            };

            audio.ProcessSource(source, 0.016f);
            
            bool valid = source.CalculatedVolume > 0.0f && source.CalculatedVolume <= 1.0f;
            Console.WriteLine($"  ✓ Echo 3D Audio Spatial Processor (Vol={source.CalculatedVolume:F2}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Spatial Audio Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestOverthinkingAIBrain()
    {
        try
        {
            var brain = new AIBrain();
            brain.SetBlackboardValue("TargetDistance", 15.0f);
            
            var val = brain.GetBlackboardValue<float>("TargetDistance");
            bool valid = val == 15.0f;
            
            Console.WriteLine($"  ✓ Overthinking AI Priority Blackboard (Val={val}): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ AI Blackboard Exception: {ex.Message}");
            return false;
        }
    }
}
