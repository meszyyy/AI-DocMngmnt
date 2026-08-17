import { useState } from 'react';

interface ChatSource {
  index: number;
  documentId: string;
  fileName: string;
  score: number;
}

interface ChatTurn {
  question: string;
  answer: string;
  sources: ChatSource[];
  error?: string;
}

export default function ChatPanel() {
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [question, setQuestion] = useState('');
  const [busy, setBusy] = useState(false);

  const updateLastTurn = (patch: (turn: ChatTurn) => ChatTurn) => {
    setTurns((current) => {
      const copy = [...current];
      copy[copy.length - 1] = patch(copy[copy.length - 1]);
      return copy;
    });
  };

  const ask = async (e: React.FormEvent) => {
    e.preventDefault();
    const q = question.trim();
    if (!q || busy) return;

    setBusy(true);
    setQuestion('');
    setTurns((t) => [...t, { question: q, answer: '', sources: [] }]);

    try {
      const response = await fetch('/api/documents/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: q }),
      });
      if (!response.ok || !response.body) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      // The server streams NDJSON: one JSON object per line. We read the
      // byte stream, cut it at newlines and apply each message as it arrives.
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          if (!line.trim()) continue;
          const msg = JSON.parse(line);
          if (msg.type === 'sources') {
            updateLastTurn((turn) => ({ ...turn, sources: msg.sources }));
          } else if (msg.type === 'delta') {
            updateLastTurn((turn) => ({ ...turn, answer: turn.answer + msg.text }));
          }
        }
      }
    } catch (err) {
      updateLastTurn((turn) => ({
        ...turn,
        error: err instanceof Error ? err.message : 'Chat failed',
      }));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="card" style={{ marginTop: '1.5rem' }}>
      <div className="section-header">
        <h2 className="section-title">Ask your documents</h2>
      </div>

      {turns.map((turn, i) => (
        <div key={i} style={{ marginBottom: '1rem' }}>
          <div style={{ fontWeight: 600 }}>❓ {turn.question}</div>
          <div style={{ whiteSpace: 'pre-wrap', marginTop: '0.4rem' }}>
            {turn.answer || (busy && i === turns.length - 1 ? 'Thinking…' : '')}
          </div>
          {turn.error && (
            <div className="error-message" role="alert">
              <span>{turn.error}</span>
            </div>
          )}
          {turn.sources.length > 0 && (
            <div style={{ display: 'flex', gap: '0.3rem', flexWrap: 'wrap', marginTop: '0.4rem' }}>
              {turn.sources.map((s) => (
                <a
                  key={s.index}
                  href={`/api/documents/${s.documentId}/content`}
                  download={s.fileName}
                  style={{
                    fontSize: '0.75em',
                    padding: '0.1rem 0.5rem',
                    borderRadius: '1rem',
                    border: '1px solid rgba(128,128,128,0.4)',
                    textDecoration: 'none',
                  }}
                >
                  [{s.index}] {s.fileName} ({s.score.toFixed(2)})
                </a>
              ))}
            </div>
          )}
        </div>
      ))}

      <form onSubmit={ask} style={{ display: 'flex', gap: '0.5rem' }}>
        <input
          type="text"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Ask a question about your documents…"
          style={{ flex: 1, padding: '0.5rem' }}
        />
        <button className="refresh-button" type="submit" disabled={busy || !question.trim()}>
          {busy ? 'Answering…' : 'Ask'}
        </button>
      </form>
    </div>
  );
}
