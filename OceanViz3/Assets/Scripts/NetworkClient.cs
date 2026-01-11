using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System;

/// <summary>
/// Handles TCP network communication with the OceanViz3 server.
/// This client maintains a persistent connection, automatically reconnecting if the connection is lost.
/// The client:
/// - Connects to a TCP server on port 48765
/// - Automatically attempts to reconnect if connection is lost
/// - Processes incoming messages in the format "MatchState:{json}"
/// - Forwards state updates to the StateMatcher component
/// </summary>

public class NetworkClient : MonoBehaviour
{
    private TcpClient tcpClient;
    private const int TCP_PORT = 48765;
    private const float RECONNECT_DELAY = 2f; // Seconds between reconnection attempts
    private bool shouldTryConnect = true;
    private StateMatcher stateMatcher;
    
    private void Start()
    {
        stateMatcher = FindObjectOfType<StateMatcher>();
        if (stateMatcher == null)
        {
            Debug.LogError("[NetworkClient] StateMatcher not found in scene");
            return;
        }
        
        TryConnectWithRetry();
    }

    private async void TryConnectWithRetry()
    {
        Debug.Log("[NetworkClient] Attempting to connect to OceanViz3 server...");
        
        while (shouldTryConnect)
        {
            if (tcpClient?.Connected != true)
            {
                await ConnectToServer("127.0.0.1");
                
                if (tcpClient?.Connected != true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(RECONNECT_DELAY));
                }
            }
            else
            {
                break;
            }
        }
    }

    private async Task ConnectToServer(string serverIP)
    {
        try
        {
            tcpClient?.Close(); // Close any existing connection
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(serverIP, TCP_PORT);
            Debug.Log("[NetworkClient] Connected to OceanViz3 server successfully");
            
            StartReceiving();
        }
        catch (SocketException e)
        {
            // Only log errors that aren't related to server being unavailable
            // Error code 10061 means "Connection refused" (no server listening)
            if (e.ErrorCode != 10061)
            {
                Debug.Log($"[NetworkClient] Connection failed: {e.Message}");
            }
        }
    }

    private async void StartReceiving()
    {
        NetworkStream stream = tcpClient.GetStream();
        byte[] lengthBuffer = new byte[4];

        while (true)
        {
            try
            {
                // Read message length first
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
                if (bytesRead < 4) break; // Connection closed
                
                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                
                // Create buffer of exact size needed
                byte[] messageBuffer = new byte[messageLength];
                int totalBytesRead = 0;
                
                // Keep reading until we have the entire message
                while (totalBytesRead < messageLength)
                {
                    bytesRead = await stream.ReadAsync(messageBuffer, totalBytesRead, 
                        messageLength - totalBytesRead);
                    if (bytesRead == 0) break; // Connection closed
                    totalBytesRead += bytesRead;
                }
                
                if (totalBytesRead < messageLength) break; // Connection closed mid-message
                
                string message = Encoding.UTF8.GetString(messageBuffer);
                ProcessMessage(message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkClient] Receive error: {e.Message}");
                break;
            }
        }

        // Connection lost, try to reconnect
        Debug.Log("[NetworkClient] Connection lost. Attempting to reconnect...");
        TryConnectWithRetry();
    }

    private void ProcessMessage(string message)
    {
        try
        {
            // Check if message starts with "MatchState:"
            if (message.StartsWith("MatchState:"))
            {
                string jsonContent = message.Substring("MatchState:".Length).Trim();
                Debug.Log($"[NetworkClient] Received MatchState RPC: {jsonContent}");
                
                if (stateMatcher != null)
                {
                    Debug.Log($"[NetworkClient] Received MatchState RPC: {jsonContent}");
                    stateMatcher.ApplyRequestedState(jsonContent);
                }
                else
                {
                    Debug.LogError("[NetworkClient] Cannot process MatchState: StateMatcher not found");
                }
            }
            else
            {
                Debug.Log($"[NetworkClient] Received unknown message: {message}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkClient] Error processing message: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        shouldTryConnect = false;
        tcpClient?.Close();
    }
}