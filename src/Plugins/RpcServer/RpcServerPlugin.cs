// Copyright (C) 2015-2026 The Neo Project.
//
// RpcServerPlugin.cs file belongs to the neo project and is free
// software distributed under the MIT software license, see the
// accompanying file LICENSE in the main directory of the
// repository or http://www.opensource.org/licenses/mit-license.php
// for more details.
//
// Redistribution and use in source and binary forms with or without
// modifications are permitted.

namespace Neo.Plugins.RpcServer;

public class RpcServerPlugin : Plugin
{
    public override string Name => "RpcServer";
    public override string Description => "Enables RPC for the node";

    private RpcServerSettings? settings;
    private bool disposed;
    private static readonly Lock syncRoot = new();
    private static readonly Dictionary<uint, RpcServer> servers = new();
    private static readonly Dictionary<uint, NeoSystem> systems = new();
    private static readonly Dictionary<uint, int> serverSettingsIndexes = new();
    private static readonly Dictionary<uint, List<object>> handlers = new();
    private static int serverIndex = 0;

    public override string ConfigFile => System.IO.Path.Combine(RootPath, "RpcServer.json");

    protected override UnhandledExceptionPolicy ExceptionPolicy => settings!.ExceptionPolicy;

    protected override void Configure()
    {
        var updatedSettings = new RpcServerSettings(GetConfiguration());
        lock (syncRoot)
        {
            settings = updatedSettings;
            foreach (var (network, server) in servers)
            {
                if (serverSettingsIndexes.TryGetValue(network, out var index) && index < settings.Servers.Count)
                    server.UpdateSettings(settings.Servers[index]);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            List<RpcServer> registeredServers;
            lock (syncRoot)
            {
                if (disposed)
                {
                    base.Dispose(disposing);
                    return;
                }
                disposed = true;
                registeredServers = [.. servers.Values];
                servers.Clear();
                systems.Clear();
                serverSettingsIndexes.Clear();
                handlers.Clear();
                serverIndex = 0;
            }
            foreach (var server in registeredServers)
                server.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnSystemLoaded(NeoSystem system)
    {
        if (settings is null) throw new InvalidOperationException("RpcServer settings are not loaded");
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var network = system.Settings.Network;
            if (systems.TryGetValue(network, out var existingSystem))
            {
                if (ReferenceEquals(existingSystem, system)) return;
                throw new InvalidOperationException(
                    $"RpcServer network {network} is already bound to a different NeoSystem instance");
            }

            if (serverIndex >= settings.Servers.Count)
            {
                Logs.RuntimeLogger.Warning("No RpcServer configuration available for this system instance");
                return;
            }

            var settingsIndex = serverIndex;
            var serverSettings = settings.Servers[settingsIndex];
            if (serverSettings.EnableCors && string.IsNullOrEmpty(serverSettings.RpcUser) == false && serverSettings.AllowOrigins.Length == 0)
            {
                Logs.RuntimeLogger.Warning("RcpServer: CORS is misconfigured!");
                Logs.RuntimeLogger.Warning("You have {EnableCors} and Basic Authentication enabled but {AllowOrigins} is empty in config.json for RcpServer. " +
                    "You must add url origins to the list to have CORS work from browser with basic authentication enabled. " +
                    "Example: \"AllowOrigins\": [\"http://{BindAddress}:{Port}\"]", serverSettings.EnableCors, serverSettings.AllowOrigins, serverSettings.BindAddress, serverSettings.Port);
            }

            var rpcServer = new RpcServer(system, serverSettings);
            try
            {
                if (handlers.TryGetValue(network, out var list))
                {
                    foreach (var handler in list)
                        rpcServer.RegisterMethods(handler);
                }

                rpcServer.StartRpcServer();
                servers.Add(network, rpcServer);
                systems.Add(network, system);
                serverSettingsIndexes.Add(network, settingsIndex);
                handlers.Remove(network);
                serverIndex++;
                Logs.RuntimeLogger.Information("RpcServer started for network {Network}", network);
            }
            catch
            {
                rpcServer.Dispose();
                throw;
            }
        }
    }

    public static void RegisterMethods(object handler, uint network)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (syncRoot)
        {
            if (servers.TryGetValue(network, out var server))
            {
                server.RegisterMethods(handler);
                return;
            }
            if (!handlers.TryGetValue(network, out var list))
            {
                list = [];
                handlers.Add(network, list);
            }
            if (!list.Any(existing => ReferenceEquals(existing, handler)))
                list.Add(handler);
        }
    }
}
