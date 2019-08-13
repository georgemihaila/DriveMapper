using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Management;

namespace DriveMapper
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (!File.Exists("user.txt"))
            {
                Console.WriteLine("\"user.txt\" not found!");
                Console.ReadKey();
                return;
            }
            string username = string.Empty;
            string password = string.Empty;
            try
            {
                string[] s = File.ReadAllLines("user.txt");
                username = s[0];
                password = s[1];
            }
            catch
            {
                Console.WriteLine("Error reading \"user.txt\". The file needs to have the following structure:\r\nLine 1:\tdomain\\username\r\nLine 2:\tpassword");
                Console.ReadKey();
                return;
            }
            string driveName = (args.Length == 1) ? args[0] : Clipboard.GetText();
            if (driveName == null || driveName == string.Empty)
            {
                Console.WriteLine("A shared drive must be specified as an argument or must be found in the clipboard.");
                Console.ReadKey();
                return;
            }
            if (!Regex.IsMatch(driveName, @"(?<=\\\\)[^\\]*"))
            {
                Console.WriteLine("\"{0}\" is not a valid drive name.", driveName);
                Console.ReadKey();
                return;
            }
            try
            {
                //If drive already mapped, just open it.
                //Else, map it to a new letter except for U.
                bool alreadyMapped = false;
                foreach(var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.Network)
                    {
                        DirectoryInfo dir = d.RootDirectory;
                        var x = GetUNCPath(dir.FullName.Substring(0, 2));
                        if (x == driveName)
                        {
                            alreadyMapped = true;
                            break;
                        }
                    }
                }
                if (alreadyMapped)
                {
                    Console.WriteLine("Drive already mapped.");
                }
                else
                {
                    List<string> availableLetters = Enumerable.Range('A', 'Z' - 'A' + 1).Select(i => (Char)i + ":").Except(DriveInfo.GetDrives().Select(s => s.Name.Replace("\\", ""))).ToList();
                    if (availableLetters.Contains("U:"))
                        availableLetters.Remove("U:");
                    var letter = availableLetters[0][0].ToString();
                    Console.WriteLine("Mapping drive to letter {0}...", letter);
                    DriveSettings.MapNetworkDrive(letter, driveName, username, password);
                    Console.WriteLine("Drive mapped.");
                }
                System.Diagnostics.Process.Start(driveName);
            }
            catch
            {
                Console.WriteLine("Error mapping drive \"{0}\"!", driveName);
                Console.ReadKey();
            }
        }

        public static string GetUNCPath(string path)
        {
            if (path.StartsWith(@"\\"))
            {
                return path;
            }

            ManagementObject mo = new ManagementObject();
            mo.Path = new ManagementPath(String.Format("Win32_LogicalDisk='{0}'", path));

            // DriveType 4 = Network Drive
            if (Convert.ToUInt32(mo["DriveType"]) == 4)
            {
                return Convert.ToString(mo["ProviderName"]);
            }
            else
            {
                return path;
            }
        }
    }

    public class DriveSettings
    {
        private enum ResourceScope
        {
            RESOURCE_CONNECTED = 1,
            RESOURCE_GLOBALNET,
            RESOURCE_REMEMBERED,
            RESOURCE_RECENT,
            RESOURCE_CONTEXT
        }
        private enum ResourceType
        {
            RESOURCETYPE_ANY,
            RESOURCETYPE_DISK,
            RESOURCETYPE_PRINT,
            RESOURCETYPE_RESERVED
        }
        private enum ResourceUsage
        {
            RESOURCEUSAGE_CONNECTABLE = 0x00000001,
            RESOURCEUSAGE_CONTAINER = 0x00000002,
            RESOURCEUSAGE_NOLOCALDEVICE = 0x00000004,
            RESOURCEUSAGE_SIBLING = 0x00000008,
            RESOURCEUSAGE_ATTACHED = 0x00000010
        }
        private enum ResourceDisplayType
        {
            RESOURCEDISPLAYTYPE_GENERIC,
            RESOURCEDISPLAYTYPE_DOMAIN,
            RESOURCEDISPLAYTYPE_SERVER,
            RESOURCEDISPLAYTYPE_SHARE,
            RESOURCEDISPLAYTYPE_FILE,
            RESOURCEDISPLAYTYPE_GROUP,
            RESOURCEDISPLAYTYPE_NETWORK,
            RESOURCEDISPLAYTYPE_ROOT,
            RESOURCEDISPLAYTYPE_SHAREADMIN,
            RESOURCEDISPLAYTYPE_DIRECTORY,
            RESOURCEDISPLAYTYPE_TREE,
            RESOURCEDISPLAYTYPE_NDSCONTAINER
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NETRESOURCE
        {
            public ResourceScope oResourceScope;
            public ResourceType oResourceType;
            public ResourceDisplayType oDisplayType;
            public ResourceUsage oResourceUsage;
            public string sLocalName;
            public string sRemoteName;
            public string sComments;
            public string sProvider;
        }
        [DllImport("mpr.dll")]
        private static extern int WNetAddConnection2
            (ref NETRESOURCE oNetworkResource, string sPassword,
            string sUserName, int iFlags);

        [DllImport("mpr.dll")]
        private static extern int WNetCancelConnection2
            (string sLocalName, uint iFlags, int iForce);

        public static void MapNetworkDrive(string sDriveLetter, string sNetworkPath, string username, string password)
        {
            //Checks if the last character is \ as this causes error on mapping a drive.
            if (sNetworkPath.Substring(sNetworkPath.Length - 1, 1) == @"\")
            {
                sNetworkPath = sNetworkPath.Substring(0, sNetworkPath.Length - 1);
            }

            NETRESOURCE oNetworkResource = new NETRESOURCE();
            oNetworkResource.oResourceType = ResourceType.RESOURCETYPE_DISK;
            oNetworkResource.sLocalName = sDriveLetter + ":";
            oNetworkResource.sRemoteName = sNetworkPath;

            //If Drive is already mapped disconnect the current 
            //mapping before adding the new mapping
            if (IsDriveMapped(sDriveLetter))
            {
                DisconnectNetworkDrive(sDriveLetter, true);
            }

            WNetAddConnection2(ref oNetworkResource, password, username, 0);
        }

        public static int DisconnectNetworkDrive(string sDriveLetter, bool bForceDisconnect)
        {
            if (bForceDisconnect)
            {
                return WNetCancelConnection2(sDriveLetter + ":", 0, 1);
            }
            else
            {
                return WNetCancelConnection2(sDriveLetter + ":", 0, 0);
            }
        }

        public static bool IsDriveMapped(string sDriveLetter)
        {
            string[] DriveList = Environment.GetLogicalDrives();
            for (int i = 0; i < DriveList.Length; i++)
            {
                if (sDriveLetter + ":\\" == DriveList[i].ToString())
                {
                    return true;
                }
            }
            return false;
        }
    }
}
