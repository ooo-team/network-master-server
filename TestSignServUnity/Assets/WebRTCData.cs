using System;

/// <summary>
/// Сообщение для signaling server'а (не определено в RFC, это наш протокол)
/// RFC WebRTC не определяет протокол signaling - это остается на усмотрение разработчика
/// </summary>
[Serializable]
public class SignalingMessage
{
    /// <summary>
    /// Тип сообщения: "offer", "answer", "ice_candidate", "peer_joined", "peer_left"
    /// Соответствует типам сообщений в WebRTC signaling
    /// </summary>
    public string type;
    
    /// <summary>
    /// ID отправителя сообщения
    /// Используется для идентификации peer'ов в комнате
    /// </summary>
    public string from;
    
    /// <summary>
    /// ID получателя сообщения (может быть пустым для broadcast)
    /// </summary>
    public string to;
    
    /// <summary>
    /// Полезная нагрузка сообщения (SDPPayload, ICECandidatePayload и т.д.)
    /// </summary>
    public object payload;
}

/// <summary>
/// SDP (Session Description Protocol) - RFC 4566
/// Описывает медиа сессию между peer'ами
/// </summary>
[Serializable]
public class SDPPayload
{
    /// <summary>
    /// Тип SDP: "offer" или "answer"
    /// Соответствует RFC 3264 (SDP Offer/Answer Model)
    /// </summary>
    public string type;
    
    /// <summary>
    /// SDP в текстовом формате согласно RFC 4566
    /// Пример: "v=0\no=- 1234567890 2 IN IP4 127.0.0.1..."
    /// Содержит информацию о медиа потоках, кодеках, сетевых адресах
    /// </summary>
    public string sdp;
}

/// <summary>
/// ICE Candidate - RFC 5245 (Interactive Connectivity Establishment)
/// Описывает возможный сетевой путь для соединения
/// </summary>
[Serializable]
public class ICECandidatePayload
{
    /// <summary>
    /// ICE candidate в текстовом формате согласно RFC 5245
    /// Пример: "candidate:1 1 UDP 2122252543 192.168.1.1 12345 typ host"
    /// Формат: candidate:foundation component-id transport-protocol priority connection-address port typ candidate-type
    /// </summary>
    public string candidate;
    
    /// <summary>
    /// SDP media description ID (sdpMid)
    /// Указывает к какой медиа секции относится candidate
    /// </summary>
    public string sdpMid;
    
    /// <summary>
    /// SDP media line index (sdpMLineIndex)
    /// Индекс медиа секции в SDP (0 для первой секции)
    /// </summary>
    public int sdpMLineIndex;
} 