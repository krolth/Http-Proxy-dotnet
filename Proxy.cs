using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace HttpProxy
{
    internal class Proxy
    {
        const string BackendUrl = "http://YOURBACKEND.cloudapp.net:10001/stream";
        private const string LocalUrl = "http://*:30084/";

        readonly StreamMode streamingMode;

        private const int RequestDispatchThreadCount = 4;
        static int _readChunkSize = 4 * 1024;

        private readonly HttpListener _httpListener = new HttpListener();
        private readonly Thread[] _requestThreads;

        internal Proxy(StreamMode mode, int bufferSize)
        {
            streamingMode = mode;
            _readChunkSize = bufferSize;

            Stream2StreamCopy.Init(_readChunkSize);

            Console.Write("\nStreaming mode: ");
            WriteLine(streamingMode.ToString(), ConsoleColor.Cyan);
            Console.Write("\nBackend Url: ");
            WriteLine(BackendUrl, ConsoleColor.Green);
            Console.Write("Listening at: ");
            WriteLine(LocalUrl + "\n", ConsoleColor.Green);
            Console.WriteLine("Buffer size:{0}\n", _readChunkSize);

            _httpListener.Prefixes.Add(LocalUrl);
            _httpListener.Start();
            _requestThreads = new Thread[RequestDispatchThreadCount];
            for (int i = 0; i < _requestThreads.Length; i++)
            {
                _requestThreads[i] = new Thread(RequestDispatchThread);
                _requestThreads[i].Start();
            }
        }

        async void RequestDispatchThread()
        {
            while (_httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    context.Response.StatusCode = (int)HttpStatusCode.OK;

                    string queryString = context.Request.RawUrl;
                    Stream backendContent = GetContent(queryString);
                    switch (streamingMode)
                    {
                        case StreamMode.FrameworkDefault:
                            await backendContent.CopyToAsync(context.Response.OutputStream);
                            break;
                        case StreamMode.FrameworkBuffer:
                            await backendContent.CopyToAsync(context.Response.OutputStream, _readChunkSize);
                            break;
                        case StreamMode.MultiWrite:
                            await Stream2StreamCopy.CopyStreamsAsync(backendContent, context.Response.OutputStream);
                            break;
                    }
                    
                    context.Response.OutputStream.Close();
                }
                catch (HttpListenerException)
                {
                    // Bail out - this happens on shutdown
                    return;
                }
                catch (Exception e)
                {
                    WriteLine("\nUnexpected exception:", ConsoleColor.Red);
                    Console.WriteLine(e.Message + "\n");
                }
            }
        }
        
        internal void Shutdown()
        {
            if (!_httpListener.IsListening)
            {
                return;
            }

            // Stop the listener
            _httpListener.Stop();

            //  Wait for all the request threads to stop
            for (int i = 0; i < _requestThreads.Length; i++)
            {
                var thread = _requestThreads[i];
                if (thread != null) thread.Join();
                _requestThreads[i] = null;
            }
        }

        // Appends the received query string to the BackendUrl
        private Stream GetContent(string backendQuerystring)
        {
            string requestUri = BackendUrl + backendQuerystring;
            Console.Write("\nRequesting: \n{0}", requestUri);

            var request = WebRequest.CreateHttp(requestUri);
            var response = (HttpWebResponse)request.GetResponse();
            {
                return response.GetResponseStream();
            }
        }

        public static void WriteLine(string text, ConsoleColor consoleColor)
        {
            var currentColor = Console.ForegroundColor;
            Console.ForegroundColor = consoleColor;
            Console.WriteLine(text);
            Console.ForegroundColor = currentColor;
        }
    }
}
