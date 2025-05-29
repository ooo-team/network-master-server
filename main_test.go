package main

import (
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestRoomAPI(t *testing.T) {
	// Сохраняем оригинальную функцию и подменяем на тестовую
	origGetClientIP := getClientIP
	getClientIP = func(r *http.Request) string {
		// Сначала проверяем тестовый заголовок
		if ip := r.Header.Get("X-Test-Remote-Addr"); ip != "" {
			return ip
		}
		// Если заголовка нет, берем из RemoteAddr
		host, _, err := net.SplitHostPort(r.RemoteAddr)
		if err != nil {
			return ""
		}
		return host
	}
	defer func() { getClientIP = origGetClientIP }()

	server := NewServer()
	ts := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			server.getRooms(w, r)
		case http.MethodPost:
			server.createRoom(w, r)
		case http.MethodPut:
			server.joinRoom(w, r)
		case http.MethodDelete:
			server.deleteRoom(w, r)
		}
	}))
	defer ts.Close()

	// Тест 1: Создание комнаты
	t.Run("Create Room", func(t *testing.T) {
		req, err := http.NewRequest(http.MethodPost, fmt.Sprintf("%s/v1/rooms?max_clients=4", ts.URL), nil)
		if err != nil {
			t.Fatalf("Failed to create request: %v", err)
		}
		req.Header.Set("X-Test-Remote-Addr", "192.168.1.1")

		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			t.Fatalf("Failed to create room: %v", err)
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			t.Errorf("Expected status OK, got %v", resp.StatusCode)
		}

		var result map[string]string
		if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
			t.Fatalf("Failed to decode response: %v", err)
		}

		roomID := result["room_id"]
		if roomID == "" {
			t.Error("Expected room_id in response")
		}

		// Тест 2: Получение информации о комнате
		t.Run("Get Room", func(t *testing.T) {
			req, err := http.NewRequest(http.MethodGet, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.1")

			resp, err := http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to get room: %v", err)
			}
			defer resp.Body.Close()

			if resp.StatusCode != http.StatusOK {
				t.Errorf("Expected status OK, got %v", resp.StatusCode)
			}

			var room Room
			if err := json.NewDecoder(resp.Body).Decode(&room); err != nil {
				t.Fatalf("Failed to decode room: %v", err)
			}

			if room.ID != roomID {
				t.Errorf("Expected room ID %s, got %s", roomID, room.ID)
			}
			if room.Host != "192.168.1.1" {
				t.Errorf("Expected host 192.168.1.1, got %s", room.Host)
			}
			if room.MaxClients != 4 {
				t.Errorf("Expected max clients 4, got %d", room.MaxClients)
			}
			// Проверяем, что хост добавлен в список клиентов
			if len(room.Clients) != 1 || room.Clients[0] != "192.168.1.1" {
				t.Errorf("Expected host 192.168.1.1 in clients list, got %v", room.Clients)
			}
		})

		// Тест 3: Присоединение клиента к комнате
		t.Run("Join Room", func(t *testing.T) {
			req, err := http.NewRequest(http.MethodPut, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.2")

			resp, err := http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to join room: %v", err)
			}
			defer resp.Body.Close()

			if resp.StatusCode != http.StatusOK {
				t.Errorf("Expected status OK, got %v", resp.StatusCode)
			}

			// Проверяем, что клиент добавился
			req, err = http.NewRequest(http.MethodGet, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.1")

			resp, err = http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to get room: %v", err)
			}
			defer resp.Body.Close()

			var room Room
			if err := json.NewDecoder(resp.Body).Decode(&room); err != nil {
				t.Fatalf("Failed to decode room: %v", err)
			}

			// Проверяем, что в списке клиентов есть и хост, и новый клиент
			if len(room.Clients) != 2 {
				t.Errorf("Expected 2 clients (host + new client), got %d", len(room.Clients))
			}
			clients := make(map[string]bool)
			for _, client := range room.Clients {
				clients[client] = true
			}
			if !clients["192.168.1.1"] {
				t.Error("Host not found in clients list")
			}
			if !clients["192.168.1.2"] {
				t.Error("New client not found in clients list")
			}
		})

		// Тест 4: Проверка максимального количества клиентов
		t.Run("Max Clients Check", func(t *testing.T) {
			maxClients := 4
			// Уже есть 2 клиента (host и 192.168.1.2), добавим ещё (maxClients-1) и попробуем добавить ещё одного сверх лимита
			for i := 3; i <= maxClients+1; i++ {
				ip := fmt.Sprintf("192.168.1.%d", i)
				req, err := http.NewRequest(http.MethodPut, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
				if err != nil {
					t.Fatalf("Failed to create request: %v", err)
				}
				req.Header.Set("X-Test-Remote-Addr", ip)

				resp, err := http.DefaultClient.Do(req)
				if err != nil {
					t.Fatalf("Failed to join room: %v", err)
				}
				defer resp.Body.Close()

				if i == maxClients+1 {
					if resp.StatusCode != http.StatusForbidden {
						t.Errorf("Expected status Forbidden when room is full, got %v", resp.StatusCode)
					}
				} else {
					if resp.StatusCode != http.StatusOK {
						t.Errorf("Expected status OK, got %v", resp.StatusCode)
					}
				}
			}
		})

		// Тест 5: Удаление комнаты
		t.Run("Delete Room", func(t *testing.T) {
			// Сначала пробуем удалить комнату с неправильным IP
			req, err := http.NewRequest(http.MethodDelete, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.2") // Неправильный IP

			resp, err := http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to delete room: %v", err)
			}
			defer resp.Body.Close()

			if resp.StatusCode != http.StatusForbidden {
				t.Errorf("Expected status Forbidden, got %v", resp.StatusCode)
			}

			// Теперь удаляем комнату с правильным IP
			req, err = http.NewRequest(http.MethodDelete, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.1") // Правильный IP

			resp, err = http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to delete room: %v", err)
			}
			defer resp.Body.Close()

			if resp.StatusCode != http.StatusOK {
				t.Errorf("Expected status OK, got %v", resp.StatusCode)
			}

			// Проверяем, что комната удалена
			req, err = http.NewRequest(http.MethodGet, fmt.Sprintf("%s/v1/rooms?room_id=%s", ts.URL, roomID), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", "192.168.1.1")

			resp, err = http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to get room: %v", err)
			}
			defer resp.Body.Close()

			if resp.StatusCode != http.StatusNotFound {
				t.Errorf("Expected status NotFound, got %v", resp.StatusCode)
			}
		})
	})

	// Тест 6: Получение списка всех комнат
	t.Run("Get All Rooms", func(t *testing.T) {
		// Очищаем все существующие комнаты
		server.mu.Lock()
		server.rooms = make(map[string]*Room)
		server.mu.Unlock()

		// Создаем несколько комнат
		for i := 0; i < 3; i++ {
			req, err := http.NewRequest(http.MethodPost, fmt.Sprintf("%s/v1/rooms?max_clients=4", ts.URL), nil)
			if err != nil {
				t.Fatalf("Failed to create request: %v", err)
			}
			req.Header.Set("X-Test-Remote-Addr", fmt.Sprintf("192.168.1.%d", i+1))

			resp, err := http.DefaultClient.Do(req)
			if err != nil {
				t.Fatalf("Failed to create room: %v", err)
			}
			resp.Body.Close()
		}

		req, err := http.NewRequest(http.MethodGet, fmt.Sprintf("%s/v1/rooms", ts.URL), nil)
		if err != nil {
			t.Fatalf("Failed to create request: %v", err)
		}
		req.Header.Set("X-Test-Remote-Addr", "192.168.1.1")

		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			t.Fatalf("Failed to get rooms: %v", err)
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			t.Errorf("Expected status OK, got %v", resp.StatusCode)
		}

		var rooms []Room
		if err := json.NewDecoder(resp.Body).Decode(&rooms); err != nil {
			t.Fatalf("Failed to decode rooms: %v", err)
		}

		if len(rooms) != 3 {
			t.Errorf("Expected 3 rooms, got %d", len(rooms))
		}

		// Проверяем, что в каждой комнате хост добавлен в список клиентов
		for _, room := range rooms {
			if len(room.Clients) != 1 || room.Clients[0] != room.Host {
				t.Errorf("Expected host %s in clients list for room %s, got %v", room.Host, room.ID, room.Clients)
			}
		}
	})
}
