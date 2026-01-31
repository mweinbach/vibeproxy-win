package main

import (
	"bytes"
	"crypto/tls"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"
)

const (
	hardTokenCap     = 32000
	minimumHeadroom  = 1024
	headroomRatio    = 0.1
	vercelGatewayHost = "ai-gateway.vercel.sh"
	anthropicVersion = "2023-06-01"
	betaThinking      = "interleaved-thinking-2025-05-14"
)

type ProxyConfig struct {
	VercelEnabled bool   `json:"vercelEnabled"`
	VercelApiKey  string `json:"vercelApiKey"`
}

func (c ProxyConfig) IsActive() bool {
	return c.VercelEnabled && strings.TrimSpace(c.VercelApiKey) != ""
}

type ConfigProvider struct {
	path string
	mu   sync.Mutex
	last time.Time
	cfg  ProxyConfig
}

func NewConfigProvider(path string) *ConfigProvider {
	return &ConfigProvider{path: path}
}

func (p *ConfigProvider) Load() ProxyConfig {
	p.mu.Lock()
	defer p.mu.Unlock()

	info, err := os.Stat(p.path)
	if err != nil {
		return p.cfg
	}

	if info.ModTime().Equal(p.last) {
		return p.cfg
	}

	data, err := os.ReadFile(p.path)
	if err != nil {
		return p.cfg
	}

	var cfg ProxyConfig
	if err := json.Unmarshal(data, &cfg); err != nil {
		return p.cfg
	}

	p.last = info.ModTime()
	p.cfg = cfg
	return cfg
}

func main() {
	listenPort := flag.Int("listen", 8317, "listen port")
	targetPort := flag.Int("target", 8318, "backend port")
	configPath := flag.String("config", "", "config file path")
	flag.Parse()

	provider := NewConfigProvider(*configPath)

	proxy := &Proxy{
		listenAddr: fmt.Sprintf("127.0.0.1:%d", *listenPort),
		targetAddr: fmt.Sprintf("127.0.0.1:%d", *targetPort),
		config:     provider,
	}

	srv := &http.Server{
		Addr:              proxy.listenAddr,
		Handler:           proxy,
		ReadHeaderTimeout: 5 * time.Second,
	}

	log.Printf("[ThinkingProxy] Listening on %s", proxy.listenAddr)
	if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
		log.Fatalf("ListenAndServe failed: %v", err)
	}
}

type Proxy struct {
	listenAddr string
	targetAddr string
	config     *ConfigProvider
}

func (p *Proxy) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	bodyBytes, _ := io.ReadAll(r.Body)
	_ = r.Body.Close()

	method := r.Method
	path := r.URL.Path

	if strings.HasPrefix(path, "/auth/cli-login") || strings.HasPrefix(path, "/api/auth/cli-login") {
		loginPath := path
		if strings.HasPrefix(path, "/api/") {
			loginPath = strings.TrimPrefix(path, "/api")
		}
		redirectURL := "https://ampcode.com" + loginPath
		log.Printf("[ThinkingProxy] Redirecting Amp CLI login to: %s", redirectURL)
		w.Header().Set("Location", redirectURL)
		w.WriteHeader(http.StatusFound)
		return
	}

	if strings.HasPrefix(path, "/provider/") {
		path = "/api" + path
		log.Printf("[ThinkingProxy] Rewriting Amp provider path: %s -> %s", r.URL.Path, path)
	}

	isProviderPath := strings.HasPrefix(path, "/api/provider/")
	isCliProxyPath := strings.HasPrefix(path, "/v1/") || strings.HasPrefix(path, "/api/v1/")
	forwardPath := path
	if r.URL.RawQuery != "" {
		forwardPath += "?" + r.URL.RawQuery
	}
	if !isProviderPath && !isCliProxyPath {
		p.forwardToAmp(w, r, forwardPath, bodyBytes)
		return
	}

	modifiedBody := bodyBytes
	thinkingEnabled := false
	if method == http.MethodPost && len(bodyBytes) > 0 {
		if transformed, enabled := processThinking(bodyBytes); transformed != nil {
			modifiedBody = transformed
			thinkingEnabled = enabled
		}
	}

	if p.config.Load().IsActive() && method == http.MethodPost && isClaudeModel(modifiedBody) {
		p.forwardToVercel(w, r, modifiedBody, thinkingEnabled)
		return
	}

	p.forwardToBackend(w, r, forwardPath, modifiedBody, thinkingEnabled)
}

func processThinking(body []byte) ([]byte, bool) {
	var payload map[string]interface{}
	if err := json.Unmarshal(body, &payload); err != nil {
		return nil, false
	}

	modelValue, ok := payload["model"].(string)
	if !ok {
		return nil, false
	}

	if !strings.HasPrefix(modelValue, "claude-") && !strings.HasPrefix(modelValue, "gemini-claude-") {
		return body, false
	}

	thinkingIndex := strings.LastIndex(modelValue, "-thinking-")
	if thinkingIndex == -1 {
		if strings.HasSuffix(modelValue, "-thinking") || strings.Contains(modelValue, "-thinking(") {
			log.Printf("[ThinkingProxy] Detected thinking model '%s' - enabling beta header", modelValue)
			return body, true
		}
		return body, false
	}

	budgetString := modelValue[thinkingIndex+len("-thinking-"):]

	cleanModel := ""
	if strings.HasPrefix(modelValue, "gemini-claude-") {
		prefix := modelValue[:thinkingIndex+len("-thinking-")]
		cleanModel = strings.TrimSuffix(prefix, "-")
	} else {
		cleanModel = modelValue[:thinkingIndex]
	}

	payload["model"] = cleanModel

	budget, err := strconv.Atoi(budgetString)
	if err != nil || budget <= 0 {
		log.Printf("[ThinkingProxy] Stripped invalid thinking suffix from '%s' -> '%s'", modelValue, cleanModel)
		return marshalJSON(payload), true
	}

	effectiveBudget := budget
	if effectiveBudget > hardTokenCap-1 {
		effectiveBudget = hardTokenCap - 1
		log.Printf("[ThinkingProxy] Adjusted thinking budget from %d to %d", budget, effectiveBudget)
	}

	payload["thinking"] = map[string]interface{}{
		"type":          "enabled",
		"budget_tokens": effectiveBudget,
	}

	tokenHeadroom := minimumHeadroom
	calculated := int(float64(effectiveBudget) * headroomRatio)
	if calculated > tokenHeadroom {
		tokenHeadroom = calculated
	}
	desiredMax := effectiveBudget + tokenHeadroom
	requiredMax := desiredMax
	if requiredMax > hardTokenCap {
		requiredMax = hardTokenCap
	}
	if requiredMax <= effectiveBudget {
		requiredMax = effectiveBudget + 1
		if requiredMax > hardTokenCap {
			requiredMax = hardTokenCap
		}
	}

	adjusted := false
	if current, ok := payload["max_tokens"].(float64); ok {
		if int(current) <= effectiveBudget {
			payload["max_tokens"] = requiredMax
		}
		adjusted = true
	}

	if current, ok := payload["max_output_tokens"].(float64); ok {
		if int(current) <= effectiveBudget {
			payload["max_output_tokens"] = requiredMax
		}
		adjusted = true
	}

	if !adjusted {
		if _, ok := payload["max_output_tokens"]; ok {
			payload["max_output_tokens"] = requiredMax
		} else {
			payload["max_tokens"] = requiredMax
		}
	}

	log.Printf("[ThinkingProxy] Transformed model '%s' -> '%s' with thinking budget %d", modelValue, cleanModel, effectiveBudget)
	return marshalJSON(payload), true
}

func marshalJSON(payload map[string]interface{}) []byte {
	data, err := json.Marshal(payload)
	if err != nil {
		return nil
	}
	return data
}

func isClaudeModel(body []byte) bool {
	var payload map[string]interface{}
	if err := json.Unmarshal(body, &payload); err != nil {
		return false
	}

	model, ok := payload["model"].(string)
	if !ok {
		return false
	}

	return strings.HasPrefix(model, "claude-") || strings.HasPrefix(model, "gemini-claude-")
}

func (p *Proxy) forwardToBackend(w http.ResponseWriter, r *http.Request, path string, body []byte, thinkingEnabled bool) {
	p.forwardRequestWithRetry(w, r, path, body, thinkingEnabled, true)
}

func (p *Proxy) forwardToVercel(w http.ResponseWriter, r *http.Request, body []byte, thinkingEnabled bool) {
	cfg := p.config.Load()
	targetURL := fmt.Sprintf("https://%s/v1/messages", vercelGatewayHost)

	transport := &http.Transport{
		TLSClientConfig: &tls.Config{ServerName: vercelGatewayHost},
	}

	p.forwardRequestWithClient(w, r, targetURL, body, thinkingEnabled, transport, func(req *http.Request) {
		req.Header.Set("x-api-key", cfg.VercelApiKey)
		req.Header.Set("anthropic-version", anthropicVersion)
		req.Header.Set("content-type", "application/json")
	})
}

func (p *Proxy) forwardToAmp(w http.ResponseWriter, r *http.Request, path string, body []byte) {
	targetURL := fmt.Sprintf("https://ampcode.com%s", path)

	transport := &http.Transport{}
	p.forwardRequestWithClient(w, r, targetURL, body, false, transport, func(req *http.Request) {
		req.Host = "ampcode.com"
	})
}

func (p *Proxy) forwardRequestWithRetry(w http.ResponseWriter, r *http.Request, path string, body []byte, thinkingEnabled bool, allowRetry bool) {
	targetURL := fmt.Sprintf("http://%s%s", p.targetAddr, path)
	resp, err := p.executeRequest(r, targetURL, body, thinkingEnabled, http.DefaultTransport, nil)
	if err != nil {
		http.Error(w, "Bad Gateway", http.StatusBadGateway)
		return
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusNotFound && allowRetry && !strings.HasPrefix(path, "/api/") && !strings.HasPrefix(path, "/v1/") {
		_ = resp.Body.Close()
		newPath := "/api" + path
		log.Printf("[ThinkingProxy] Got 404 for %s, retrying with %s", path, newPath)
		p.forwardRequestWithRetry(w, r, newPath, body, thinkingEnabled, false)
		return
	}

	for name, values := range resp.Header {
		for _, value := range values {
			w.Header().Add(name, value)
		}
	}
	w.WriteHeader(resp.StatusCode)
	_, _ = io.Copy(w, resp.Body)
}

func (p *Proxy) forwardRequestWithClient(w http.ResponseWriter, r *http.Request, targetURL string, body []byte, thinkingEnabled bool, transport http.RoundTripper, tweak func(req *http.Request)) {
	resp, err := p.executeRequest(r, targetURL, body, thinkingEnabled, transport, tweak)
	if err != nil {
		http.Error(w, "Bad Gateway", http.StatusBadGateway)
		return
	}
	defer resp.Body.Close()

	if isAmpRequest(targetURL) {
		p.writeAmpResponse(w, resp)
		return
	}

	for name, values := range resp.Header {
		for _, value := range values {
			w.Header().Add(name, value)
		}
	}
	w.WriteHeader(resp.StatusCode)
	_, _ = io.Copy(w, resp.Body)
}

func (p *Proxy) executeRequest(original *http.Request, targetURL string, body []byte, thinkingEnabled bool, transport http.RoundTripper, tweak func(req *http.Request)) (*http.Response, error) {
	req, err := http.NewRequest(original.Method, targetURL, bytes.NewReader(body))
	if err != nil {
		return nil, err
	}

	copyHeaders(req.Header, original.Header)
	stripHopHeaders(req.Header)

	if thinkingEnabled {
		beta := req.Header.Get("anthropic-beta")
		if beta == "" {
			req.Header.Set("anthropic-beta", betaThinking)
		} else if !strings.Contains(beta, betaThinking) {
			req.Header.Set("anthropic-beta", beta+","+betaThinking)
		}
	}

	if tweak != nil {
		tweak(req)
	}

	client := &http.Client{Transport: transport}
	return client.Do(req)
}

func (p *Proxy) writeAmpResponse(w http.ResponseWriter, resp *http.Response) {
	data, err := io.ReadAll(resp.Body)
	if err != nil {
		w.WriteHeader(resp.StatusCode)
		return
	}

	body := data
	contentType := resp.Header.Get("Content-Type")
	if strings.Contains(contentType, "text") || strings.Contains(contentType, "json") || strings.Contains(contentType, "html") {
		text := string(body)
		text = strings.ReplaceAll(text, "\r\nLocation: https://ampcode.com/", "\r\nLocation: /api/")
		text = strings.ReplaceAll(text, "\r\nLocation: http://ampcode.com/", "\r\nLocation: /api/")
		text = strings.ReplaceAll(text, "Domain=.ampcode.com", "Domain=localhost")
		text = strings.ReplaceAll(text, "Domain=ampcode.com", "Domain=localhost")
		body = []byte(text)
	}

	for name, values := range resp.Header {
		if strings.EqualFold(name, "Content-Length") {
			continue
		}
		if strings.EqualFold(name, "Location") {
			for _, value := range values {
				value = strings.ReplaceAll(value, "https://ampcode.com/", "/api/")
				value = strings.ReplaceAll(value, "http://ampcode.com/", "/api/")
				w.Header().Add(name, value)
			}
			continue
		}
		if strings.EqualFold(name, "Set-Cookie") {
			for _, value := range values {
				value = strings.ReplaceAll(value, "Domain=.ampcode.com", "Domain=localhost")
				value = strings.ReplaceAll(value, "Domain=ampcode.com", "Domain=localhost")
				w.Header().Add(name, value)
			}
			continue
		}
		for _, value := range values {
			w.Header().Add(name, value)
		}
	}
	w.Header().Set("Content-Length", strconv.Itoa(len(body)))
	w.WriteHeader(resp.StatusCode)
	_, _ = w.Write(body)
}

func isAmpRequest(targetURL string) bool {
	return strings.Contains(targetURL, "ampcode.com")
}

func copyHeaders(dst, src http.Header) {
	for name, values := range src {
		for _, value := range values {
			dst.Add(name, value)
		}
	}
}

func stripHopHeaders(header http.Header) {
	for _, key := range []string{"Connection", "Proxy-Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Te", "Trailer", "Transfer-Encoding", "Upgrade"} {
		header.Del(key)
	}
}
