using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace ServerisHTTP
{
    internal class Settings
    {
        /// <summary>
        /// Taken from: https://www.ietf.org/rfc/rfc1700.txt
        /// </summary>
        private const int defaultHTTPPort = 80;

        #region Attribute definition, initialization
        /// <summary>
        /// Marks properties that should get their values from the *.config file.
        /// </summary>
        [AttributeUsage(AttributeTargets.Property)]
        private class KeyAttribute : Attribute
        {
            public string Value { get; }

            /// <summary></summary>
            /// <param name="value">Key in *.config, corresponding this property.</param>
            public KeyAttribute(string value) { Value = value; }
        }

        static Settings()
        {
            var settings = ConfigurationManager.AppSettings;
            foreach (var propInfo in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Concat(typeof(Settings).GetProperties(BindingFlags.NonPublic | BindingFlags.Static)))
            {
                if (propInfo.CanWrite)
                {
                    try
                    {
                        var keyAttr = propInfo.GetCustomAttributes(typeof(KeyAttribute), false)
                            .FirstOrDefault() as KeyAttribute;
                        if (keyAttr == null) continue;
                        string valueStr = settings[keyAttr.Value];
                        if (!String.IsNullOrEmpty(valueStr))
                        {
                            if (propInfo.PropertyType == typeof(string))
                            {
                                propInfo.SetValue(null, valueStr);
                            }
                            else
                            {
                                object parsedValue;
                                if (propInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<byte>).GetGenericTypeDefinition())
                                {
                                    var innerType = propInfo.PropertyType.GetGenericArguments().Single();
                                    parsedValue = ParseValueString(valueStr, innerType);
                                }
                                else
                                {
                                    parsedValue = ParseValueString(valueStr, propInfo.PropertyType);
                                    if (parsedValue == null) throw new ArgumentException(keyAttr.Value);
                                }
                                if (parsedValue != null)
                                {
                                    try
                                    {
                                        propInfo.SetValue(null, parsedValue);
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new Exception("Could not use config value " + keyAttr.Value + ": " + ex.Message, ex);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to init property " + propInfo.Name + ": " + ex.Message);
                    }
                }
            }
        }

        /// <summary>Triggers the static constructor.</summary>
        internal static void Init() { }

        private static object ParseValueString(string valueStr, Type innerType)
        {
            if (innerType == typeof(int))
            {
                int res;
                if (int.TryParse(valueStr, out res)) return res;
                return null;
            }
            throw new NotImplementedException(innerType.Name);
        }
        #endregion

        #region MyListener
        private static string _rawListenerIP;
        [Key("ListenerIP")]
        private static string RawListenerIP
        {
            get { return _rawListenerIP; }
            set
            {
                string strIP;
                int? parsedPort;
                int idx = value.IndexOf(':');
                if (idx >= 0)
                {
                    strIP = value.Substring(0, idx);
                    int port;
                    if (int.TryParse(value.Substring(idx + 1), out port) && port > 0) parsedPort = port;
                    else parsedPort = null;
                }
                else
                {
                    strIP = value;
                    parsedPort = null;
                }
                ListenerIP = IPAddress.Parse(strIP);
                if (parsedPort.HasValue) ListenerPort1 = parsedPort.Value;
                _rawListenerIP = value;
            }
        }

        public static IPAddress ListenerIP { get; private set; } = IPAddress.Loopback;

        /// <summary>Port that was specified with the IP.</summary>
        private static int ListenerPort1 { get; set; } = defaultHTTPPort;

        /// <summary>Port that was specified separately from the IP.</summary>
        [Key("ListenerPort")]
        private static int? ListenerPort2 { get; set; }

        public static int ListenerPort { get { return ListenerPort2 ?? ListenerPort1; } }

        /// <summary>
        /// </summary>
        /// <remarks>
        /// Apparently, <see cref="SocketOptionName.MaxConnections"/> is not supported:
        /// <para/>https://msdn.microsoft.com/en-us/library/system.net.sockets.socketoptionname(v=vs.113).aspx
        /// <para/>This seems to be a constant and not an option name:
        /// <para/>http://referencesource.microsoft.com/#System/net/System/Net/Sockets/SocketOptionName.cs,5573622ffc246068
        /// </remarks>
        [Key("ConnectionQueueLimit")]
        public static int ConnectionQueueLimit { get; private set; } = 10;

        /// <summary>Max amount of concurrent connections.</summary>
        [Key("ConnectionLimit")]
        public static int ConnectionLimit { get; private set; } = 10;

        [Key("TransmissionTimeout")]
        public static int TransmissionTimeout { get; private set; } = 1000;
        #endregion

        #region HTTPService
        private static string _rootDir = Environment.CurrentDirectory;
        /// <summary>Where to look for web pages.</summary>
        [Key("HTTPRootDirectory")]
        public static string HTTPRootDirectory
        {
            get { return _rootDir; }
            private set
            {
                if (Path.IsPathRooted(value)) _rootDir = value;
                _rootDir = Path.Combine(Environment.CurrentDirectory, value);
            }
        }

        /// <summary></summary>
        [Key("HTTPDefaultPage")]
        public static string HTTPDefaultPage { get; private set; } = "index.html";

        [Key("RedirectToIndex")]
        public static bool RedirectToIndex { get; private set; } = false;
        #endregion
    }
}
