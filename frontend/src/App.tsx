import { useState, useEffect, useCallback } from 'react';
import './App.css';

interface DocumentDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status: string;
  uploadedAt: string;
  summary: string | null;
  tags: string[];
}

function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [newFileName, setNewFileName] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchDocuments = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch('/api/documents');
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      setDocuments(await response.json());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch documents');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchDocuments();
  }, [fetchDocuments]);

  const createDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newFileName.trim()) return;
    try {
      const response = await fetch('/api/documents', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName: newFileName.trim() }),
      });
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      setNewFileName('');
      await fetchDocuments();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create document');
    }
  };

  const deleteDocument = async (id: string) => {
    try {
      const response = await fetch(`/api/documents/${id}`, { method: 'DELETE' });
      if (!response.ok && response.status !== 404) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      await fetchDocuments();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete document');
    }
  };

  const formatDate = (dateString: string) =>
    new Date(dateString).toLocaleString();

  return (
    <div className="app-container">
      <header className="app-header">
        <h1 className="app-title">AI Document Manager</h1>
        <p className="app-subtitle">Learning project — .NET Aspire + React</p>
      </header>

      <main className="main-content">
        <section aria-labelledby="documents-heading">
          <div className="card">
            <div className="section-header">
              <h2 id="documents-heading" className="section-title">
                Documents
              </h2>
              <button
                className="refresh-button"
                onClick={fetchDocuments}
                disabled={loading}
                type="button"
              >
                {loading ? 'Loading...' : 'Refresh'}
              </button>
            </div>

            <form onSubmit={createDocument} style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
              <input
                type="text"
                value={newFileName}
                onChange={(e) => setNewFileName(e.target.value)}
                placeholder="File name (e.g. contract.pdf)"
                style={{ flex: 1, padding: '0.5rem' }}
              />
              <button className="refresh-button" type="submit" disabled={!newFileName.trim()}>
                Add
              </button>
            </form>

            {error && (
              <div className="error-message" role="alert">
                <span>{error}</span>
              </div>
            )}

            {documents.length === 0 && !loading ? (
              <p>No documents yet — add one above!</p>
            ) : (
              <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                <thead>
                  <tr>
                    <th style={{ padding: '0.5rem' }}>File name</th>
                    <th style={{ padding: '0.5rem' }}>Status</th>
                    <th style={{ padding: '0.5rem' }}>Uploaded</th>
                    <th style={{ padding: '0.5rem' }}></th>
                  </tr>
                </thead>
                <tbody>
                  {documents.map((doc) => (
                    <tr key={doc.id} style={{ borderTop: '1px solid rgba(128,128,128,0.3)' }}>
                      <td style={{ padding: '0.5rem' }}>{doc.fileName}</td>
                      <td style={{ padding: '0.5rem' }}>{doc.status}</td>
                      <td style={{ padding: '0.5rem' }}>{formatDate(doc.uploadedAt)}</td>
                      <td style={{ padding: '0.5rem', textAlign: 'right' }}>
                        <button type="button" onClick={() => deleteDocument(doc.id)}>
                          Delete
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </section>
      </main>
    </div>
  );
}

export default App;
