# HoneyDrunk.Knowledge

The runtime behind the knowledge contracts. For now it splits a document into passages.

Chunking takes plain text or Markdown, treated alike. Paragraphs are packed up to 1,200 characters; a longer paragraph is cut at the last space. A document holds at most 1,024 passages. HTML is not parsed here: pass text.
