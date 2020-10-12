using FASTER.core;
using Hagar.Invocation;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CallLog
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var path = Path.GetTempPath() + "FasterLogSample\\";
            IDevice device = Devices.CreateLogDevice(path + "hlog.log");

            // FasterLog will recover and resume if there is a previous commit found
            using var log = new FasterLog(new FasterLogSettings { LogDevice = device });

            await log.EnqueueAndWaitForCommitAsync()
        }
    }

    internal abstract class LogWriterBase
    {
        private readonly FasterLog _dbLog;

        protected LogWriterBase(FasterLog dbLog)
        {
            _dbLog = dbLog;
        }

        protected void SendRequest(IResponseCompletionSource callback, IInvokable body)
        {
            callback.Complete()
        }
    }
}
