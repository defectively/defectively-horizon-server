using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Defectively.Core.Common;
using Defectively.Core.Communication;
using Defectively.Core.Extensibility;
using Defectively.Core.Extensibility.Events;
using Defectively.Core.Models;
using Defectively.Core.Networking;
using Defectively.Core.Networking.Udp;
using Defectively.Core.Storage;

namespace Defectively.HorizonServer
{
    public class ServerWrapper : IServerWrapper
    {
        private UdpReceiver receiver = new UdpReceiver(52000);
        public Server Server { get; set; }

        public async Task Initialize() {
            ComponentPool.ServerWrapper = this;
            DataStorage.Instance.Directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            DataStorage.Instance.Load();

            Server = new Server(42000);
            Server.Connected += OnConnected;
            Server.Disconnected += OnDisconnected;

            receiver.UdpMessageReceived += OnUdpMessageReceived;

            foreach (var extPath in Directory.EnumerateFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions"), "*.dll")) {
                ExtensionManager.InitializeExtension(extPath, true);
            }


            new Task(async () => { await receiver.ReceiveAsync(); }).Start();
            await Server.StartAsync(true);
        }

        private void OnUdpMessageReceived(UdpReceiver sender, UdpMessageReceivedEventArgs e) {
            if (e.Content == "ml.festival.defectively.scan") {
                UdpSender.SendMessage("ml.festival.defectively.scanResponse:42000", e.RemoteEndPoint.Address.ToString(), 52001);
            }
        }

        private async void OnConnected(ConnectableBase sender, ConnectedEventArgs e) {
            await EventService.InvokeEvent(EventType.Connected, new ConnectedEvent { Client = e.Client }, this);

            string id = null;
            byte[] password = null;
            var package = await e.Client.ReadAsync<Package>();

            if (package.Type == PackageType.Information) {
                await e.Client.WriteAsync(new Package(PackageType.Information, new ServerInformation {
                    AcceptsClients = true,
                    ClientExtensions = ExtensionManager.Extensions.FindAll(_ => _.CreateClientInstance).Select(_ => _.Name),
                    Extensions = ExtensionManager.Extensions.Select(_ => _.Name),
                    Name = "Festival Defectively Horizon Dev Server",
                    Url = "https://festival.ml/"
                }));

                return;
            }

            if (package.Type == PackageType.Development) {
                e.Client.Account = new Account {
                    AresFlags = new AresStorage("*"),
                    Id = package.GetContent<string>(0),
                    Name = package.GetContent<string>(1),
                    TeamId = package.GetContent<string>(2)
                };

                await e.Client.WriteAsync(new Package(PackageType.Success, e.Client.Account));

                foreach (var extPath in ExtensionManager.Extensions.FindAll(_ => _.CreateClientInstance).Select(_ => _.ExtensionPath).Distinct()) {
                    await e.Client.WriteAsync(new Package(PackageType.Assembly, File.ReadAllBytes(extPath), new FileInfo(extPath).Name));
                }

                await Listen(e.Client);

                return;
            }

            try {
                id = package.GetContent<string>(0);
                password = package.GetContent<byte[]>(1);
            } catch { }

            if (string.IsNullOrEmpty(id) || password?.Length == 0) {
                await e.Client.WriteAsync(new Package(PackageType.Error, "no_auth_data"));
                e.Client.Disconnect();
                return;
            }

            var account = DataStorage.Instance.Accounts.Find(_ => _.Id == id);

            if (account == null) {
                await e.Client.WriteAsync(new Package(PackageType.Error, "account_unknown"));
                e.Client.Disconnect();
                return;
            }

            if (!account.Password.SequenceEqual(password)) {
                await e.Client.WriteAsync(new Package(PackageType.Error, "password_invalid"));
                e.Client.Disconnect();
                return;
            }

            e.Client.Account = account;
            await e.Client.WriteAsync(new Package(PackageType.Success, account));

            await EventService.InvokeEvent(EventType.Authenticated, new AuthenticatedEvent { Account = account }, this);

            foreach (var extPath in ExtensionManager.Extensions.FindAll(_ => _.CreateClientInstance).Select(_ => _.ExtensionPath).Distinct()) {
                await e.Client.WriteAsync(new Package(PackageType.Assembly, File.ReadAllBytes(extPath), new FileInfo(extPath).Name));
            }

            await Listen(e.Client);
        }

        private async Task Listen(Client client) {
            try {
                while (client.IsAlive) {
                    var package = await client.ReadAsync<Package>();

                    var @event = new PackageReceivedEvent { EndpointId = client.Account.Id, Package = package };
                    await EventService.InvokeEvent(EventType.PackageReceived, @event, this);

                    if (@event.SkipLegacyHandling)
                        continue;

                    switch (package.Type) {
                    case PackageType.ExternalEvent:
                        await EventService.InvokeEvent(EventType.External, package.GetContent<ExternalEvent>(0), this);
                        break;
                    }
                }
            } catch { }
        }

        private void OnDisconnected(ConnectableBase sender, DisconnectedEventArgs e) { }

        public async Task SendPackageTo(Package package, string accountId) { }

        public async Task SendPackageToAccounts(Package package, params string[] accountIds) {
            Server.Clients.FindAll(_ => accountIds.Contains(_.Account.Id)).ForEach(async _ => await _.WriteAsync(package));
        }

        public async Task SendPackageToAccountsWithFlag(Package package, string aresFlag) { }
        public async Task SendPackageToTeams(Package package, params string[] teamIds) { }
        public async Task SendPackageToChannels(Package package, params string[] channelIds) { }
        public async Task SendPackageToAll(Package package) { }
    }
}