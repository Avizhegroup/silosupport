# RAG Chat — External Integration Guide - Silo

This document is for **external clients** integrating with SiloAI's Retrieval-Augmented
Generation (RAG) chat feature. It explains how the feature works end-to-end and describes
every public HTTP endpoint, including the input payloads and the responses each one returns.

---

## 1. What is RAG Chat?

RAG (Retrieval-Augmented Generation) lets the AI assistant answer questions using your own
uploaded knowledge documents instead of (or in addition to) its general knowledge. The
answer is generated only from the most relevant pieces ("chunks") of text retrieved from the
documents you uploaded, plus any active instructions configured for that document type.

The feature is made of two cooperating pipelines:

1. **Indexing pipeline** — turns an uploaded document into searchable vector data.
2. **Chat pipeline** — turns a user question into a grounded, cited answer.

---

## 2. Indexing pipeline (how documents become searchable)

1. **Upload** — the client sends a file (`.txt` or `.md` by default, configurable) to
   `POST /api/rag/documents`.
2. **Storage** — the API computes a SHA-256 hash of the file and stores document metadata
   (`RagDocument` record) with `ProcessingStatus = Pending`.
3. **Text extraction** — the raw bytes are converted to plain text (extractor is chosen by
   content type/extension, e.g. Markdown or plain text extractor).
4. **Chunking** — the extracted text is split into overlapping chunks sized in tokens
   (default target ≈ 800 tokens per chunk, ≈ 100 tokens overlap between adjacent chunks),
   splitting on paragraph/sentence boundaries so meaning isn't cut mid-sentence.
5. **Embedding** — each chunk's text is sent to the embedding model (OpenAI
   `text-embedding-3-small` by default) to produce a numeric vector.
6. **Persistence** — chunks and their vectors are stored (`RagDocumentChunk` rows, vector
   stored in a SQL Server `VECTOR` column) and the document's `ProcessingStatus` is updated to
   `Completed` (or `Failed`, with the error message recorded, if any step throws).

Re-uploading new content for an existing document is done via the **rebuild** endpoint, which
deletes the old chunks/embeddings first and re-runs the same pipeline.

---

## 3. Chat pipeline (how a question becomes an answer)

1. **Start or resume a conversation.** A client either starts a new conversation
   (`POST /api/rag/chat/new-session`) or reuses a previously returned `conversationId` on
   subsequent calls to `send`.
2. **Retrieve relevant chunks.** For every message sent, the server:
   - Generates an embedding for the user's question.
   - Runs a cosine-similarity vector search (`VECTOR_DISTANCE`) against the stored chunks,
     optionally filtered by `docType` and/or `key`, returning the top `K` most similar chunks
     (`topK`, default 5, clamped between 1 and 20).
3. **Load active instructions.** Any active `RagInstruction` records matching the requested
   `docType` are appended to the system prompt, letting you customize behavior per document
   type without code changes.
4. **Augment the prompt.** The retrieved chunks (with file name, chunk index and similarity
   score) and the user's question are merged into a single augmented message using a template.
   - When `isMainChat` is `true`, source markers/file names are omitted from the message sent
     to the model and no citations are returned — useful for a "general assistant" experience
     that quietly uses the knowledge base without exposing document references.
   - When `isMainChat` is `false` (default), each retrieved chunk is labeled (e.g. `[1]`,
     `[2]`, …) and the model is instructed to cite sources.
5. **Call the AI model.** The augmented message is sent to the configured RAG chat model
   (`OpenAI:RagModel`) through the conversation session (previous turns are restored from the
   stored session state so the model retains context across calls).
6. **Persist conversation state.** The updated session state is saved to the database, keyed
   by conversation id and an owner key derived from the caller's identity (customer id, user
   id, or username claim from the JWT/API key), so only the owning caller can continue that
   conversation.
7. **Return the answer.** The response text, conversation id, citations (empty when
   `isMainChat` is `true`), and token usage are returned to the client.

If a client passes a `conversationId` that doesn't exist or doesn't belong to the caller, the
API returns `404 Not Found`.

---

## 4. Authentication

All endpoints below require one of:

- **JWT authentication** — standard HTTP `Authorization` header using the JWT scheme
  (scheme name followed by the access token).
- **API key** (RAG chat endpoints only) — `X-Api-Key: <key>` header. The key is hashed
  (SHA-256) and validated against active, non-expired keys; if the key is linked to a
  customer, the conversation is scoped to that customer.

---

## 5. Endpoints

### 5.1 RAG Chat

Base route: `api/rag/chat`

#### `POST /api/rag/chat/new-session`

Starts a brand-new RAG conversation without sending a message yet.

**Input:** none (empty body).

**Output** (`RagChatResponse`):

| Field            | Type              | Description                                  |
|------------------|-------------------|-----------------------------------------------|
| `responseText`   | string            | Empty for a new session.                      |
| `conversationId` | guid              | Id to reuse on subsequent `send` calls.       |
| `citations`      | array             | Empty for a new session.                      |
| `tokenUsage`     | object \| null    | Not populated for a new session.              |

#### `POST /api/rag/chat/send`

Sends a user message, retrieves relevant knowledge, and returns the AI's grounded answer.

**Input** (`RagChatRequest`, JSON body):

| Field            | Type      | Required | Default          | Description                                                                 |
|------------------|-----------|----------|------------------|-------------------------------------------------------------------------------|
| `conversationId` | guid?     | no       | `null`           | Existing conversation to continue. Omit to start a new one implicitly.       |
| `message`        | string    | **yes**  | —                | The user's question.                                                          |
| `topK`           | int       | no       | `5`              | Number of chunks to retrieve (clamped 1–20).                                  |
| `isMainChat`     | bool      | no       | `false`          | `true` hides citations/source markers for a general-assistant style answer.  |
| `docType`        | enum      | no       | `GeneralChat`    | One of `GeneralChat`, `Report`, `Image`, `PageAgent`. Filters retrieval and active instructions. |
| `key`            | string?   | no       | `null`           | Optional additional partition/tenant key to filter which documents are searched. |

**Output** (`RagChatResponse`):

| Field            | Type                     | Description                                                                 |
|------------------|--------------------------|-------------------------------------------------------------------------------|
| `responseText`   | string                   | The AI-generated answer.                                                      |
| `conversationId` | guid                     | Conversation id (new or continued) — store it to continue the chat.          |
| `citations`      | array of citation object | Sources used (empty when `isMainChat` is `true`). See below.                  |
| `tokenUsage`     | object \| null           | Token accounting for the model call. See below.                              |

Citation object (`RagChatCitationDto`):

| Field         | Type   | Description                                   |
|---------------|--------|-------------------------------------------------|
| `chunkId`     | guid   | Id of the retrieved chunk.                      |
| `documentId`  | guid   | Id of the source document.                      |
| `fileName`    | string | Original uploaded file name.                    |
| `category`    | string?| Document category, if set.                      |
| `chunkIndex`  | int    | Position of the chunk within the document.      |
| `similarity`  | double | Cosine similarity score (`1 - distance`), higher is more relevant. |
| `snippet`     | string | Truncated excerpt of the chunk content (max 280 chars). |

Token usage object (`ChatTokenUsageDto`):

| Field                    | Type | Description                          |
|--------------------------|------|----------------------------------------|
| `inputTokenCount`        | long | Tokens consumed by the prompt.        |
| `outputTokenCount`       | long | Tokens consumed by the completion.    |
| `cachedInputTokenCount`  | long | Tokens served from prompt cache.      |
| `totalTokenCount`        | long | Sum of input and output tokens.       |

**Error responses:**

- `400 Bad Request` — `message` is missing/blank.
- `404 Not Found` — `conversationId` does not exist or does not belong to the caller.

---

### 5.2 RAG Documents (Knowledge Base Management)

Base route: `api/rag/documents` (JWT only).

#### `GET /api/rag/documents`

Lists all uploaded documents.

**Input:** none.

**Output:** array of `RagDocumentDto`:

| Field               | Type      | Description                                             |
|---------------------|-----------|-----------------------------------------------------------|
| `id`                | guid      | Document id.                                              |
| `fileName`          | string    | Internal stored file name (randomized).                   |
| `originalFileName`  | string    | Name as uploaded by the client.                            |
| `contentType`       | string    | MIME type.                                                 |
| `docType`           | enum      | `GeneralChat` / `Report` / `Image` / `PageAgent`.          |
| `key`               | string?   | Optional partition/tenant key.                              |
| `category`          | string?   | Optional free-text category.                                |
| `tags`              | string?   | Optional free-text tags.                                    |
| `fileHash`          | string    | SHA-256 hash of the file content.                           |
| `fileSize`          | long      | File size in bytes.                                         |
| `processingStatus`  | string    | `Pending` / `Processing` / `Completed` / `Failed`.          |
| `processingError`   | string?   | Error message if processing failed.                         |
| `chunkCount`        | int       | Number of chunks generated.                                 |
| `createDateTime`    | datetime  | Upload time (UTC).                                          |
| `creatorUserId`     | string?   | Uploader's identity.                                         |
| `lastUpdateDateTime`| datetime? | Last processing/update time (UTC).                          |

#### `GET /api/rag/documents/{id}`

Gets full details for one document, including its chunks.

**Input:** `id` (guid, route parameter).

**Output:** `RagDocumentDetailsDto` — all fields of `RagDocumentDto` above, plus:

| Field    | Type                        | Description                    |
|----------|-----------------------------|----------------------------------|
| `chunks` | array of `RagDocumentChunkDto` | The document's indexed chunks. |

Returns `404 Not Found` if the document doesn't exist.

#### `POST /api/rag/documents`

Uploads and indexes a new document (`multipart/form-data`).

**Input (form fields):**

| Field      | Type    | Required | Description                                                     |
|------------|---------|----------|-------------------------------------------------------------------|
| `file`     | file    | **yes**  | The document to upload. Must not exceed the configured max size (default 25 MB) and must use a supported extension (default `.txt`, `.md`). |
| `docType`  | enum?   | no       | `GeneralChat` / `Report` / `Image` / `PageAgent`. Defaults to `GeneralChat`. |
| `key`      | string? | no       | Optional partition/tenant key used to scope retrieval later.      |
| `category` | string? | no       | Optional free-text category.                                       |
| `tags`     | string? | no       | Optional free-text tags.                                            |

**Output:** `RagUploadResponseDto`:

| Field              | Type    | Description                                    |
|--------------------|---------|--------------------------------------------------|
| `documentId`       | guid    | Id of the newly created document.                |
| `chunkCount`        | int     | Number of chunks generated during indexing.      |
| `processingStatus` | string  | Final status after indexing (`Completed`/`Failed`). |
| `processingError`  | string? | Error message if indexing failed.                |

**Error responses:** `400 Bad Request` if the file is missing/empty, exceeds the max size, or has an unsupported extension.

#### `DELETE /api/rag/documents/{id}`

Deletes a document and its chunks.

**Input:** `id` (guid, route parameter).

**Output:** `204 No Content` on success, `404 Not Found` if the document doesn't exist.

#### `POST /api/rag/documents/{id}/rebuild`

Replaces a document's content and re-runs indexing (deletes existing chunks first).

**Input:** `id` (guid, route parameter) + `multipart/form-data` with a required `file` field.

**Output:** `RagUploadResponseDto` (same shape as upload).

**Error responses:** `400 Bad Request` if the file is missing/empty or exceeds the max size; `404 Not Found` if the document doesn't exist.

#### `POST /api/rag/documents/search`

Runs a raw similarity search without invoking the chat model — useful for previewing what the
chat pipeline would retrieve.

**Input** (`RagSearchRequest`, JSON body):

| Field     | Type    | Required | Default | Description                                   |
|-----------|---------|----------|---------|--------------------------------------------------|
| `query`   | string  | **yes**  | —       | Text to search for.                              |
| `topK`    | int     | no       | `10`    | Number of results to return.                     |
| `docType` | enum?   | no       | `null`  | Optional filter by document type.                |
| `key`     | string? | no       | `null`  | Optional filter by partition/tenant key.         |

**Output:** array of `RagSearchHitDto`:

| Field        | Type   | Description                                     |
|--------------|--------|----------------------------------------------------|
| `chunkId`    | guid   | Id of the matched chunk.                          |
| `documentId` | guid   | Id of the source document.                        |
| `fileName`   | string | Original uploaded file name.                       |
| `category`   | string?| Document category, if set.                          |
| `chunkIndex` | int    | Position of the chunk within the document.          |
| `content`    | string | Full chunk text.                                    |
| `distance`   | double | Cosine distance (lower = more similar).             |
| `similarity` | double | `1 - distance` (higher = more similar).             |

**Error responses:** `400 Bad Request` if `query` is missing/blank.

---

### 5.3 RAG Instructions

Base route: `api/rag/instructions` (JWT only). Instructions are extra system-prompt text
applied per document type (e.g. tone, formatting, or domain rules) and are automatically
merged into the RAG chat system prompt for matching `docType` when `isActive` is `true`.

#### `GET /api/rag/instructions`

Lists all instructions.

**Output:** array of `RagInstructionDto` (see fields below).

#### `GET /api/rag/instructions/{id}`

Gets one instruction by id. Returns `404 Not Found` if missing.

`RagInstructionDto` fields:

| Field               | Type      | Description                                    |
|---------------------|-----------|---------------------------------------------------|
| `id`                | guid      | Instruction id.                                    |
| `docType`           | enum      | Document type this instruction applies to.         |
| `key`               | string?   | Optional partition/tenant key.                      |
| `category`          | string?   | Optional free-text category.                        |
| `tags`              | string?   | Optional free-text tags.                             |
| `content`           | string    | The instruction text merged into the system prompt.|
| `isActive`          | bool      | Whether it's currently applied.                     |
| `createDateTime`    | datetime  | Creation time (UTC).                                |
| `lastUpdateDateTime`| datetime? | Last update time (UTC).                             |

#### `POST /api/rag/instructions`

Creates a new instruction.

**Input** (`CreateRagInstructionCommand`, JSON body):

| Field      | Type    | Required | Description                              |
|------------|---------|----------|---------------------------------------------|
| `docType`  | enum?   | no       | Document type this instruction applies to.  |
| `key`      | string? | no       | Optional partition/tenant key.               |
| `category` | string? | no       | Optional free-text category.                  |
| `tags`     | string? | no       | Optional free-text tags.                        |
| `content`  | string  | **yes**  | The instruction text.                           |

**Output:** the created `RagInstructionDto`.

#### `PUT /api/rag/instructions/{id}`

Updates an existing instruction. **Input:** `id` (route) + same body shape as create, plus
`isActive`. **Output:** the updated `RagInstructionDto`, or `404 Not Found` if it doesn't exist.

#### `DELETE /api/rag/instructions/{id}`

Deletes an instruction. **Output:** `204 No Content` on success, `404 Not Found` if missing.

---

## 6. Typical client workflow

1. Upload one or more knowledge documents: `POST /api/rag/documents`.
2. (Optional) Add per-`docType` instructions: `POST /api/rag/instructions`.
3. Start a conversation: `POST /api/rag/chat/new-session` → store `conversationId`.
4. Send messages: `POST /api/rag/chat/send` with the stored `conversationId`, reading
   `responseText` and `citations` from each response.
5. (Optional) Use `POST /api/rag/documents/search` to debug/preview retrieval quality
   independent of the chat model.
