using Neo.Json;

namespace Neo.Plugins.RpcServer.Tests;

[TestClass]
public sealed class UT_ParameterConverter_WitnessSize
{
    private const int MaxWitnessScriptLength = 1024;

    [TestMethod]
    public void ToSignersAndWitnesses_MaximumWitnessScripts_AreAccepted()
    {
        var result = CreateParameters(MaxWitnessScriptLength, MaxWitnessScriptLength)
            .ToSignersAndWitnesses(ProtocolSettings.Default.AddressVersion);

        Assert.AreEqual(MaxWitnessScriptLength, result.Witnesses[0].InvocationScript.Length);
        Assert.AreEqual(MaxWitnessScriptLength, result.Witnesses[0].VerificationScript.Length);
    }

    [TestMethod]
    public void ToSignersAndWitnesses_OversizedWitnessScript_IsRejected()
    {
        var parameters = CreateParameters(MaxWitnessScriptLength + 1, 0);

        Assert.ThrowsExactly<RpcException>(
            () => parameters.ToSignersAndWitnesses(ProtocolSettings.Default.AddressVersion));
    }

    private static JArray CreateParameters(int invocationLength, int verificationLength) =>
    [
        new JObject
        {
            ["account"] = "0x1234567890abcdef1234567890abcdef12345678",
            ["scopes"] = "CalledByEntry",
            ["invocation"] = Convert.ToBase64String(new byte[invocationLength]),
            ["verification"] = Convert.ToBase64String(new byte[verificationLength]),
        },
    ];
}
