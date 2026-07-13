using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.Json;

namespace Neo.Plugins.RpcServer.Tests;

[TestClass]
public sealed class UT_RpcServer_Http
{
    [TestMethod]
    public async Task RegisteredMethods_RealHttp_ReturnValuesNullAndOfficialErrors()
    {
        RuntimeHelpers.RunClassConstructor(typeof(NeoSystem).TypeHandle);
        DisposeAndClearAutoLoadedPlugins();

        using var system = new NeoSystem(CreateProtocolSettings());
        var port = ReserveLoopbackPort();
        using var server = new RpcServer(system, RpcServersSettings.Default with
        {
            BindAddress = IPAddress.Loopback,
            Port = checked((ushort)port),
            EnableCors = false,
        });
        server.RegisterMethods(new TestMethods());
        server.StartRpcServer();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

        var value = await CallAsync(client, "testecho", "neo-n4");
        Assert.AreEqual("neo-n4", value["result"]!.AsString());

        var nullResult = await CallAsync(client, "testnull");
        Assert.IsNull(nullResult["error"]);
        Assert.IsNull(nullResult["result"]);

        var missing = await CallAsync(client, "testecho");
        Assert.AreEqual(RpcError.InvalidParams.Code, (int)missing["error"]!["code"]!.AsNumber());

        DisposeAndClearAutoLoadedPlugins();
    }

    private static async Task<JObject> CallAsync(HttpClient client, string method, params object?[] parameters)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = method,
            ["params"] = new JArray(parameters.Select(ToJsonToken)),
        };
        using var response = await client.PostAsync(
            "",
            new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return JToken.Parse(await response.Content.ReadAsStringAsync()) as JObject
            ?? throw new InvalidDataException("RPC response was not a JSON object");
    }

    private static JToken? ToJsonToken(object? value) => value switch
    {
        null => JToken.Null,
        string text => text,
        int number => number,
        _ => throw new ArgumentException($"Unsupported test parameter type: {value.GetType()}", nameof(value)),
    };

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ProtocolSettings CreateProtocolSettings() => ProtocolSettings.Default with
    {
        Network = 0x4E34_5253,
        StandbyCommittee =
        [
            ECPoint.Parse(
                "0278ed78c917797b637a7ed6e7a9d94e8c408444c41ee4c0a0f310a256b9271eda",
                ECCurve.Secp256r1),
        ],
        ValidatorsCount = 1,
        SeedList = [],
    };

    private static void DisposeAndClearAutoLoadedPlugins()
    {
        foreach (var plugin in Plugin.Plugins.ToArray())
        {
            try { plugin.Dispose(); }
            catch { }
        }
        Plugin.Plugins.Clear();
    }

    private sealed class TestMethods
    {
        [RpcMethod(Name = "testecho")]
        public JToken Echo(string value) => value;

        [RpcMethod(Name = "testnull")]
        public JToken? Null() => JToken.Null;
    }
}
