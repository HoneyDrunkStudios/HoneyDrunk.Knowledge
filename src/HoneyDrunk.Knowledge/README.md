# HoneyDrunk.Knowledge

The runtime behind the knowledge contracts. It splits a document into passages, embeds them through the embedding generator and retrieves by similarity over any `IKnowledgeStore`.

Chunking takes plain text or Markdown, treated alike. Paragraphs are packed up to 1,200 characters; a longer paragraph is cut at the last space. A document holds at most 1,024 passages. HTML is not parsed here: pass text.
