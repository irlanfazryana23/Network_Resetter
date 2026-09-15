using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetworkResetter
{
    internal sealed class AdapterInfo
    {
        public string Id, Name, Description, IPv4, DnsServers;
        public bool Connected, HasIPv4, HasIPv6, HasGateway, HasDns, LinkLocalIPv4;
        public bool? Dhcp;
        public override string ToString() { return Name + (Connected ? " - connected" : " - disconnected"); }
        public string Summary
        {
            get
            {
                return Description + "\nIPv4: " + IPv4 + "  |  " +
                    (Dhcp == true ? "Automatic IP (DHCP)" : Dhcp == false ? "Manual IP / DHCP off" : "IPv4 DHCP status unavailable") +
                    "\nDNS servers: " + DnsServers;
            }
        }
        public string Advice
        {
            get
            {
                if (!Connected) return "Connect to Wi-Fi or plug in Ethernet, then check again.";
                if (Dhcp == true && !HasIPv4)
                    return "No usable DHCP IPv4 address" + (LinkLocalIPv4 ? " (169.254.x.x)" : "") +
                        ". Try Quick repair. If it fails, check the router or contact IT." +
                        (HasIPv6 ? " IPv6 may still work." : "");
                if (!HasDns) return "No DNS server listed. Try Quick repair for DHCP, or check your manual DNS settings.";
                if (!HasGateway) return "No default gateway listed. Check the router or your manual IP settings.";
                return "IP and DNS settings are present. If websites still fail, try Quick repair.";
            }
        }
    }

    internal sealed class CheckResult
    {
        public List<AdapterInfo> Adapters;
        public string DnsResult;
    }

    internal static class NetworkChecks
    {
        // Called only by the explicit Check connection button or repair preflight.
        public static List<AdapterInfo> ReadAdapters()
        {
            var list = new List<AdapterInfo>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                try
                {
                    IPInterfaceProperties properties = nic.GetIPProperties();
                    var ipv4 = properties.UnicastAddresses.Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
                    bool? dhcp = null;
                    try { IPv4InterfaceProperties v4 = properties.GetIPv4Properties(); if (v4 != null) dhcp = v4.IsDhcpEnabled; }
                    catch (NetworkInformationException) { }
                    var dns = properties.DnsAddresses.Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).ToList();
                    list.Add(new AdapterInfo
                    {
                        Id = nic.Id, Name = nic.Name, Description = nic.Description,
                        Connected = nic.OperationalStatus == OperationalStatus.Up, Dhcp = dhcp,
                        IPv4 = ipv4.Count == 0 ? "Not assigned" : string.Join(", ", ipv4),
                        HasIPv4 = ipv4.Any(a => !IsLinkLocal(a) && !a.Equals(IPAddress.Any) && !IPAddress.IsLoopback(a)),
                        LinkLocalIPv4 = ipv4.Any(IsLinkLocal),
                        HasIPv6 = properties.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal && !a.Address.Equals(IPAddress.IPv6Any) && !IPAddress.IsLoopback(a.Address)),
                        HasGateway = properties.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)),
                        HasDns = dns.Count != 0, DnsServers = dns.Count == 0 ? "None listed" : string.Join(", ", dns)
                    });
                }
                catch (NetworkInformationException)
                {
                    list.Add(new AdapterInfo { Id = nic.Id, Name = nic.Name, Description = "Windows could not read this adapter. Check again.", IPv4 = "Unknown", DnsServers = "Unknown" });
                }
            }
            return list.OrderByDescending(a => a.Connected).ThenBy(a => a.Name).ToList();
        }
        private static bool IsLinkLocal(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
        }
        public static async Task<CheckResult> CheckAsync()
        {
            List<AdapterInfo> adapters = await Task.Run(() => ReadAdapters());
            Task<string> probe = ResolveName();
            Task finished = await Task.WhenAny(probe, Task.Delay(8000));
            return new CheckResult
            {
                Adapters = adapters,
                DnsResult = finished == probe ? await probe : "DNS lookup timed out. Check the connection, DNS settings, or VPN."
            };
        }
        private static async Task<string> ResolveName()
        {
            // Catch here so even a timed-out probe cannot leave an unobserved task fault.
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync("www.microsoft.com");
                return addresses.Length > 0
                    ? "DNS lookup completed for www.microsoft.com. The result may be cached and does not confirm internet access."
                    : "DNS returned no address for www.microsoft.com. Try Quick repair.";
            }
            catch (Exception ex)
            {
                return "DNS lookup failed for www.microsoft.com. Details: " + ex.Message;
            }
        }
        public static AdapterInfo ValidateDhcpTarget(string id)
        {
            AdapterInfo target = ReadAdapters().FirstOrDefault(a => a.Id == id);
            if (target == null || !target.Connected || target.Dhcp != true)
                throw new InvalidOperationException("The adapter changed or DHCP is off. Check again before repairing.");
            if (target.Name.IndexOfAny(new[] { '"', '*', '?', '\r', '\n', '\0' }) >= 0 || target.Name.EndsWith("\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsupported adapter name. Rename it in Windows, or turn off Renew automatic IP.");
            return target;
        }
    }
}
