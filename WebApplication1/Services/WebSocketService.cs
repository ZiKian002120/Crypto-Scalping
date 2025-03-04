using System;
using WebSocketSharp;
using Microsoft.Extensions.Logging;

namespace WebApplication1.Services
{
    public class WebSocketService
    {
        private readonly ILogger<WebSocketService> _logger;
        private WebSocket _webSocket;

        public event EventHandler<MessageEventArgs> OnMessage;

        public WebSocketService(ILogger<WebSocketService> logger)
        {
            _logger = logger;
        }

        public void Connect(string url)
        {
            _webSocket = new WebSocket(url);
            _webSocket.OnMessage += WebSocket_OnMessage;
            _webSocket.OnError += WebSocket_OnError;
            _webSocket.OnClose += WebSocket_OnClose;
            _webSocket.Connect();
        }

        private void WebSocket_OnMessage(object sender, MessageEventArgs e)
        {
            _logger.LogInformation($"Received message: {e.Data}");
            OnMessage?.Invoke(this, e); // Raise the OnMessage event
        }

        private void WebSocket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            _logger.LogError($"WebSocket error: {e.Message}");
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            _logger.LogInformation($"WebSocket closed: {e.Reason}");
        }

        public void Send(string message)
        {
            _webSocket.Send(message);
        }

        public void Close()
        {
            _webSocket.Close();
        }
    }
}