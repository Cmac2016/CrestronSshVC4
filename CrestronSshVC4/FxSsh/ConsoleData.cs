using FxSsh.Services;
using System;
using System.Linq;

namespace FxSsh
{
    public static class ConsoleData
    {
        internal static Channel _channel { get; set; }

        private static event EventHandler<DataChangedEventsArgs> _onDataReceived = delegate { };

        public static event EventHandler<DataChangedEventsArgs> OnDataReceived
        {
            add
            {
                if (!_onDataReceived.GetInvocationList().Contains(value))
                {
                    _onDataReceived += value;
                }
            }
            remove => _onDataReceived -= value;
        }

        internal static void DataFromConsole(string data)
        {
            if (_onDataReceived != null)
            {
                _onDataReceived(null,new DataChangedEventsArgs(data));
            }
        }

        public static void ConsoleReply(string data)
        {
            _channel.SendData(System.Text.Encoding.UTF8.GetBytes($"\r\n{data}\r\n"));
        }
    }
    public class DataChangedEventsArgs : EventArgs
    {
        public string data { get; set; }

        public DataChangedEventsArgs()
        {
        }

        public DataChangedEventsArgs(string newData)
        {
            this.data = newData;
        }
    }
}
