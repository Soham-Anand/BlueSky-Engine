using System;

namespace BlueSky.Tests;

public static class EngineTestSuiteRunner
{
    public static bool RunFullEngineDiagnosticSuite()
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine("🌌 BLUESKY ENGINE - MASTER SUBSYSTEM DIAGNOSTIC SUITE");
        Console.WriteLine("===============================================================");
        
        bool allPassed = true;
        
        allPassed &= ECSTests.RunAllTests();
        allPassed &= PolarisSIMDTests.RunAllTests();
        allPassed &= TeaScriptTests.RunAllTests();
        allPassed &= AssetImporterTests.RunAllTests();
        allPassed &= PhysicsVehicleTests.RunAllTests();
        allPassed &= AudioAITests.RunAllTests();
        allPassed &= NetworkingTests.RunAllTests();
        allPassed &= StrataCodecTests.RunAllTests();
        allPassed &= StrataImporterTests.RunAllTests();
        allPassed &= StrataSurfaceUploadTests.RunAllTests();
        allPassed &= StrataSkyTests.RunAllTests();
        allPassed &= StrataProbeTests.RunAllTests();
        allPassed &= StrataImportWriterTests.RunAllTests();
        allPassed &= StrataPackTests.RunAllTests();
        allPassed &= StrataConfiguratorTests.RunAllTests();
        allPassed &= EditorStabilityTests.RunAllTests();

        Console.WriteLine("\n===============================================================");
        if (allPassed)
        {
            Console.WriteLine("🎉 ALL ENGINE SUBSYSTEM TESTS PASSED SUCCESSFULLY! (100% HEALTH)");
        }
        else
        {
            Console.WriteLine("⚠️ SOME ENGINE SUBSYSTEM TESTS FAILED. PLEASE CHECK LOGS ABOVE.");
        }
        Console.WriteLine("===============================================================\n");

        return allPassed;
    }
}
