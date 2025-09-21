using System;

/// <summary>
/// Простое сообщение для обмена между пирами через signaling сервер
/// Содержит всё необходимое для WebRTC соединения
/// </summary>
[Serializable]
public class SignalingMessage
{
    public string type;    // "offer", "answer", "ice_candidate", "peer_joined", "peer_left"
    public string from;    // Кто отправил
    public string to;      // Кому отправить (пустой = всем)
    public string payload; // Данные сообщения в JSON
}

/// <summary>
/// Информация о новом пире в комнате
/// Приходит когда кто-то подключается
/// </summary>
[Serializable]
public class PeerJoinedData
{
    public string peer_id;      // ID нового пира
    public string[] all_peers;  // Все пиры в комнате сейчас
} 