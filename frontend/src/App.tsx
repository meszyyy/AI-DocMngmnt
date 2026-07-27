import { useState, useEffect, useCallback, useRef } from 'react';
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

interface SearchResultDto {
  documentId: string;
  fileName: string;
  snippet: string;
  score: number;
}

function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [searching, setSearching] = useState(false);
  const [searchResults, setSearchResults] = useState<SearchResultDto[] | null>(null);
  // The query the current results belong to (the input may change afterwards).
  const [resultsQuery, setResultsQuery] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);

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

  // While any document is still being processed, poll the list so the
  // status transitions (Uploaded -> Processing -> Processed) show up live.
  useEffect(() => {
    const hasActive = documents.some(
      (d) => d.status === 'Uploaded' || d.status === 'Processing',
    );
    if (!hasActive) return;
    const timer = setInterval(fetchDocuments, 3000);
    return () => clearInterval(timer);
  }, [documents, fetchDocuments]);

  const uploadFile = async (file: File) => {
    setUploading(true);
    setError(null);
    try {
      const formData = new FormData();
      // The key must match the IFormFile parameter name on the server ("file").
      formData.append('file', file);

      const response = await fetch('/api/documents', {
        method: 'POST',
        body: formData,
      });
      if (!response.ok) {
        throw new Error(`Upload failed! status: ${response.status}`);
      }
      await fetchDocuments();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload file');
    } finally {
      setUploading(false);
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    }
  };

  const onFileSelected = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      uploadFile(file);
    }
  };

  const search = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;
    setSearching(true);
    setError(null);
    try {
      const response = await fetch(`/api/documents/search?q=${encodeURIComponent(query.trim())}`);
      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }
      setSearchResults(await response.json());
      setResultsQuery(query.trim());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed');
    } finally {
      setSearching(false);
    }
  };

  const clearSearch = () => {
    setSearchResults(null);
    setQuery('');
    setResultsQuery('');
  };

  // Wraps case-insensitive occurrences of the query words in <mark>.
  // Semantic matches may not contain the words literally — then nothing
  // is highlighted, which is honest.
  const highlight = (text: string, forQuery: string) => {
    const words = forQuery
      .split(/\s+/)
      .map((w) => w.replace(/[^\p{L}\p{N}]/gu, ''))
      .filter((w) => w.length >= 3)
      .map((w) => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
    if (words.length === 0) return text;

    // With a capturing group, split() keeps the matches at odd indices.
    const parts = text.split(new RegExp(`(${words.join('|')})`, 'giu'));
    return parts.map((part, i) =>
      i % 2 === 1 ? (
        <mark key={i} style={{ backgroundColor: '#ffd54f', color: '#000', borderRadius: '2px' }}>
          {part}
        </mark>
      ) : (
        part
      ),
    );
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

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    return `${(bytes / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
  };

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
              <div style={{ display: 'flex', gap: '0.5rem' }}>
                <button
                  className="refresh-button"
                  onClick={() => fileInputRef.current?.click()}
                  disabled={uploading}
                  type="button"
                >
                  {uploading ? 'Uploading...' : 'Upload file'}
                </button>
                <button
                  className="refresh-button"
                  onClick={fetchDocuments}
                  disabled={loading}
                  type="button"
                >
                  {loading ? 'Loading...' : 'Refresh'}
                </button>
              </div>
            </div>

            <input
              ref={fileInputRef}
              type="file"
              onChange={onFileSelected}
              style={{ display: 'none' }}
            />

            <form onSubmit={search} style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
              <input
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Semantic search (e.g. 'project budget')"
                style={{ flex: 1, padding: '0.5rem' }}
              />
              <button className="refresh-button" type="submit" disabled={searching || !query.trim()}>
                {searching ? 'Searching...' : 'Search'}
              </button>
              {searchResults !== null && (
                <button className="refresh-button" type="button" onClick={clearSearch}>
                  Clear
                </button>
              )}
            </form>

            {error && (
              <div className="error-message" role="alert">
                <span>{error}</span>
              </div>
            )}

            {searchResults !== null && (
              <div style={{ marginBottom: '1.5rem' }}>
                <h3 style={{ marginBottom: '0.5rem' }}>Search results</h3>
                {searchResults.length === 0 ? (
                  <p>No matches found.</p>
                ) : (
                  searchResults.map((r, i) => (
                    <div
                      key={`${r.documentId}-${i}`}
                      style={{
                        border: '1px solid rgba(128,128,128,0.3)',
                        borderRadius: '0.5rem',
                        padding: '0.75rem',
                        marginBottom: '0.5rem',
                      }}
                    >
                      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                        <a href={`/api/documents/${r.documentId}/content`} download={r.fileName}>
                          <strong>{r.fileName}</strong>
                        </a>
                        <span style={{ opacity: 0.6, fontSize: '0.85em' }}>
                          score: {r.score.toFixed(3)}
                        </span>
                      </div>
                      <div style={{ fontSize: '0.9em', opacity: 0.85, marginTop: '0.4rem' }}>
                        {highlight(r.snippet, resultsQuery)}
                      </div>
                    </div>
                  ))
                )}
              </div>
            )}

            {documents.length === 0 && !loading ? (
              <p>No documents yet — upload one!</p>
            ) : (
              <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                <thead>
                  <tr>
                    <th style={{ padding: '0.5rem' }}>File name</th>
                    <th style={{ padding: '0.5rem' }}>Size</th>
                    <th style={{ padding: '0.5rem' }}>Status</th>
                    <th style={{ padding: '0.5rem' }}>Uploaded</th>
                    <th style={{ padding: '0.5rem' }}></th>
                  </tr>
                </thead>
                <tbody>
                  {documents.map((doc) => (
                    <tr key={doc.id} style={{ borderTop: '1px solid rgba(128,128,128,0.3)' }}>
                      <td style={{ padding: '0.5rem' }}>
                        <a href={`/api/documents/${doc.id}/content`} download={doc.fileName}>
                          {doc.fileName}
                        </a>
                        {doc.summary && (
                          <div style={{ fontSize: '0.85em', opacity: 0.75, marginTop: '0.25rem' }}>
                            {doc.summary}
                          </div>
                        )}
                        {doc.tags.length > 0 && (
                          <div style={{ display: 'flex', gap: '0.3rem', flexWrap: 'wrap', marginTop: '0.3rem' }}>
                            {doc.tags.map((tag) => (
                              <span
                                key={tag}
                                style={{
                                  fontSize: '0.75em',
                                  padding: '0.1rem 0.5rem',
                                  borderRadius: '1rem',
                                  border: '1px solid rgba(128,128,128,0.4)',
                                }}
                              >
                                {tag}
                              </span>
                            ))}
                          </div>
                        )}
                      </td>
                      <td style={{ padding: '0.5rem' }}>{formatBytes(doc.sizeBytes)}</td>
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
