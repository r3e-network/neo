using Neo.Persistence;
using Neo.SmartContract.Native;

namespace Neo.Plugins.RpcServer.Model;

internal sealed class AssetDescriptor
{
    internal byte Decimals { get; }

    internal AssetDescriptor(IReadOnlyStore snapshot, UInt160 assetId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var token = NativeContract.TokenManagement.GetTokenInfo(snapshot, assetId)
            ?? throw new ArgumentException(
                $"No token metadata found for asset id {assetId}.",
                nameof(assetId));
        Decimals = token.Decimals;
    }
}
