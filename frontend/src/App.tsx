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

function App() {
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
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

            {error && (
              <div className="error-message" role="alert">
                <span>{error}</span>
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
