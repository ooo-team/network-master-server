
### Цель:
- WebSocket сигналинг-сервер
- STUN/TURN сервер через `pion/turn`
- REST API для авторизации (опционально)

---

### I. Signaling Server (WebSocket)

#### 1. Структура проекта
```
/signaling
  - main.go
  - server.go
  - room.go
  - peer.go
  - types.go
```

#### 2. Библиотеки
```go
import (
  "github.com/gorilla/websocket"
  "net/http"
  "encoding/json"
  "sync"
)
```

#### 3. Типы
```go
type Peer struct {
    ID       string
    Conn     *websocket.Conn
    RoomCode string
}

type SignalMessage struct {
    Type    string          `json:"type"`
    From    string          `json:"from"`
    To      string          `json:"to"`
    Payload json.RawMessage `json:"payload"`
}
```

#### 4. Комнаты
```go
type Room struct {
    Code  string
    Peers map[string]*Peer
    Mutex sync.Mutex
}
```

#### 5. Эндпоинты
```go
http.HandleFunc("/ws", handleWebSocket)
http.ListenAndServe(":8080", nil)
```

#### 6. WebSocket хендлер
```go
func handleWebSocket(w http.ResponseWriter, r *http.Request) {
    conn, _ := upgrader.Upgrade(w, r, nil)
    peer := &Peer{Conn: conn}

    for {
        var msg SignalMessage
        if err := conn.ReadJSON(&msg); err != nil {
            break
        }

        // find recipient peer and forward
        target := FindPeerByID(msg.To)
        if target != nil {
            target.Conn.WriteJSON(msg)
        }
    }
}
```

#### 🔍 Тестирование Signal-сервера:
- Запустить `go run main.go`
- Подключить тестовый WebSocket клиент (например, через [websocat](https://github.com/vi/websocat) или Postman)
- Отправить mock-сообщения между двумя клиентами с `type: "offer"`, `type: "candidate"`, проверить пересылку

---

### II. STUN/TURN Server (pion/turn)

#### 1. Установка
```bash
go get github.com/pion/turn
```

#### 2. Пример запуска
```go
s, err := turn.NewServer(turn.ServerConfig{
    Realm: "game",
    AuthHandler: func(username string, realm string, srcAddr net.Addr) ([]byte, bool) {
        password := "secret"
        key := turn.GenerateAuthKey(username, realm, password)
        return key, true
    },
    ListeningPort: 3478,
    ListeningIP: net.ParseIP("0.0.0.0"),
})
defer s.Close()
```

#### 🔍 Тестирование TURN-сервера:
- Использовать `trickle-ice` тестер: https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
- Вставить настройки STUN/TURN, убедиться, что получаются кандидаты типа `relay` (TURN работает)

---

### III. REST API (опционально)
```go
type AuthRequest struct {
    Login    string `json:"login"`
    Password string `json:"password"`
}

http.HandleFunc("/api/login", func(w http.ResponseWriter, r *http.Request) {
    var req AuthRequest
    json.NewDecoder(r.Body).Decode(&req)
    // Проверка логина, генерация JWT и возврат
})
```

#### 🔍 Тестирование API:
- Поднять сервер
- Отправить `POST /api/login` через curl/Postman
- Проверить, что возвращается JWT/токен

---

### IV. Деплой
- Открыть порты: 8080 (ws), 3478 (UDP/TCP)
- Сервер может быть на домашнем неттопе
