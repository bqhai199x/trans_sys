(function () {
    const defaultPlaceholder = '[\n  "Bản dịch 1",\n  "Bản dịch 2"\n]';

    const multipartStates = new WeakMap();
    const activeStates = new Set();

    function release(state) {
        state.cancelWorker?.();
        state.urls.forEach(value => URL.revokeObjectURL(value));
        state.panel?.remove();
        activeStates.delete(state);
    }

    // Match exact endpoint so concurrent operations cannot display each other's attachments.
    function findExportBlock(url) {
        let pathName;
        try { pathName = new URL(url, window.location.href).pathname.replace(/\/$/, '').toLowerCase(); }
        catch { return null; }
        const blocks = document.querySelectorAll('.opblock-post');
        for (const block of blocks) {
            const pathEl = block.querySelector('.opblock-summary-path');
            const path = pathEl ? pathEl.textContent.trim().toLowerCase() : '';
            if (path && pathName === path) {
                return block;
            }
        }
        return null;
    }

    function beginResponse(url) {
        const block = findExportBlock(url);
        if (!block) return null;
        const previous = multipartStates.get(block);
        if (previous) release(previous);
        block.classList.remove('has-multipart-result', 'has-response-preview');
        const state = { block, urls: [], panel: null };
        multipartStates.set(block, state);
        activeStates.add(state);
        return state;
    }

    function mountPanel(state) {
        const body = state.block.querySelector('.opblock-body');
        if (!state.block.classList.contains('is-open') || !body) {
            state.panel?.remove();
        } else if (state.panel && state.panel.parentNode !== body) {
            body.appendChild(state.panel);
        }
    }

    function prepareInWorker(buffer, contentType, state) {
        return new Promise(resolve => {
            let worker;
            const finish = value => {
                worker?.terminate();
                state.cancelWorker = null;
                resolve(value);
            };
            state.cancelWorker = () => finish(null);
            try {
                worker = new Worker('/swagger-response-worker.js');
                worker.onmessage = event => finish(event.data);
                worker.onerror = () => finish({ error: 'Không đọc được preview. Hãy tải response đầy đủ.' });
                worker.postMessage({ buffer, contentType }, [buffer]);
            } catch {
                finish({ error: 'Preview không khả dụng. Hãy tải response đầy đủ.' });
            }
        });
    }

    function download(state, parent, blob, filename) {
        const link = document.createElement('a');
        const objectUrl = URL.createObjectURL(blob);
        state.urls.push(objectUrl);
        link.href = objectUrl;
        link.download = filename;
        link.textContent = 'Tải ' + filename + ' (' + blob.size.toLocaleString('vi-VN') + ' byte)';
        parent.appendChild(link);
    }

    function showResponse(result, error, originalBlob, headers, state) {
        if (multipartStates.get(state.block) !== state || !activeStates.has(state)) return;
        const panel = document.createElement('details');
        panel.className = 'multipart-result';
        const title = document.createElement('summary');
        title.textContent = (result?.kind === 'multipart' ? 'Kết quả multipart' : 'Response đầy đủ') + ' · ' + originalBlob.size.toLocaleString('vi-VN') + ' byte';
        panel.appendChild(title);
        const note = document.createElement('p');
        note.textContent = error || (result.preview.truncated
            ? 'Preview đã rút gọn để giữ giao diện phản hồi. Tải JSON để xem đầy đủ.'
            : 'Mở để xem JSON và tải riêng từng tệp.');
        panel.appendChild(note);
        const links = document.createElement('div');
        links.className = 'response-downloads';
        panel.appendChild(links);
        if (result) {
            result.parts.forEach(part => download(state, links, new Blob([part.bytes], { type: part.contentType }),
                part.filename || (part.id === '<metadata>' ? 'metadata.json' : part.id === '<units>' ? 'units.json' : 'download')));
            if (result.kind === 'multipart') state.block.classList.add('has-multipart-result');
        }
        if (!result || result.kind === 'multipart') download(state, links, originalBlob,
            /^multipart\//i.test(originalBlob.type) ? 'response.multipart' : 'response.json');
        const previewText = error || result.preview.text;
        let rendered = false;
        panel.addEventListener('toggle', () => {
            if (!panel.open || rendered) return;
            rendered = true;
            const preview = document.createElement('pre');
            preview.textContent = previewText;
            panel.appendChild(preview);
            const headerPanel = document.createElement('details');
            const headerTitle = document.createElement('summary');
            headerTitle.textContent = 'Header response gốc';
            headerPanel.appendChild(headerTitle);
            const headerText = document.createElement('pre');
            headerText.textContent = Array.from(headers, ([name, value]) => name + ': ' + value).join('\n');
            headerPanel.appendChild(headerText);
            panel.appendChild(headerPanel);
        });
        state.panel = panel;
        state.block.classList.add('has-response-preview');
        mountPanel(state);
    }

    // Swagger receives only a bounded display body. Full network bytes remain downloadable.
    async function prepareResponse(response, state) {
        const type = response.headers.get('content-type') || '';
        if (!state || !/^(multipart\/mixed|application\/(?:[\w.+-]+\+)?json)(?:\s*;|$)/i.test(type)) return response;
        const isMultipart = /^multipart\//i.test(type);
        const buffer = await response.arrayBuffer();
        const options = { status: response.status, statusText: response.statusText, headers: response.headers };
        if (!isMultipart && buffer.byteLength <= window.FileHandlerResponse.maxInlineBytes) return new Response(buffer, options);
        const originalBlob = new Blob([buffer], { type });
        if (multipartStates.get(state.block) === state && activeStates.has(state)) {
            const prepared = await prepareInWorker(buffer, type, state);
            if (prepared) showResponse(prepared.result, prepared.error, originalBlob, response.headers, state);
        }
        const headers = new Headers(response.headers);
        headers.set('Content-Type', 'text/plain; charset=utf-8');
        for (const name of ['Content-Length', 'Content-Encoding', 'Content-Disposition']) headers.delete(name);
        return new Response('Preview Swagger đã rút gọn. Mở mục kết quả bên dưới để xem hoặc tải response đầy đủ và header gốc.', { ...options, headers });
    }

    // Helper to find textarea in a given block or globally
    function getExportTextarea(url) {
        if (url) {
            const block = findExportBlock(url);
            if (block) {
                const ta = block.querySelector('textarea.json-schema-textarea, textarea[name="texts"], textarea');
                if (ta) return ta;
            }
        }
        return document.activeElement && document.activeElement.tagName === 'TEXTAREA'
            ? document.activeElement
            : document.querySelector('.opblock-post.is-open textarea.json-schema-textarea, textarea.json-schema-textarea, textarea[name="texts"]');
    }

    // 1. Intercept window.fetch: guarantee that multipart FormData contains the complete texts from textarea
    const originalFetch = window.fetch;
    window.fetch = function (input, init) {
        const url = typeof input === 'string' ? input : (input && input.url ? input.url : '');
        if (url.toLowerCase().includes('/export')) {
            const body = init ? init.body : (input ? input.body : null);
            if (body instanceof FormData) {
                const ta = getExportTextarea(url);
                if (ta && ta.value && ta.value.trim().length > 0) {
                    const val = ta.value.trim();
                    body.set('texts', val);
                }
            }
        }
        const state = beginResponse(url);
        return originalFetch.apply(this, arguments).then(response => prepareResponse(response, state));
    };

    // 2. Intercept XMLHttpRequest as fallback
    const originalXHROpen = window.XMLHttpRequest.prototype.open;
    window.XMLHttpRequest.prototype.open = function (method, url) {
        this._url = typeof url === 'string' ? url : '';
        return originalXHROpen.apply(this, arguments);
    };

    const originalXHRSend = window.XMLHttpRequest.prototype.send;
    window.XMLHttpRequest.prototype.send = function (body) {
        if (body instanceof FormData && this._url && this._url.toLowerCase().includes('/export')) {
            const ta = getExportTextarea(this._url);
            if (ta && ta.value && ta.value.trim().length > 0) {
                const val = ta.value.trim();
                body.set('texts', val);
            }
        }
        const state = beginResponse(this._url);
        if (state) this.addEventListener('load', () => {
            const type = this.getResponseHeader('Content-Type') || '';
            if (!/^multipart\/mixed(?:\s*;|$)/i.test(type)) return;
            if (this.response instanceof Blob || this.response instanceof ArrayBuffer)
                void prepareResponse(new Response(this.response, { status: this.status, headers: { 'Content-Type': type } }), state);
        }, { once: true });
        return originalXHRSend.apply(this, arguments);
    };

    // 3. DOM Enhancer: Upgrades single-line input to spacious textarea with monospace styling across all export endpoints
    function enhance() {
        activeStates.forEach(state => { if (!state.block.isConnected) release(state); });
        const exportBlocks = document.querySelectorAll('.opblock-post[id*="export" i], .opblock-post');
        exportBlocks.forEach(block => {
            const state = multipartStates.get(block);
            if (state?.panel) mountPanel(state);
            const pathEl = block.querySelector('.opblock-summary-path');
            const path = pathEl ? pathEl.textContent.trim().toLowerCase() : '';
            const id = (block.id || '').toLowerCase();
            if (!path.includes('/export') && !id.includes('export')) return;

            const rows = block.querySelectorAll('tr, .parameters-col_name');
            rows.forEach(el => {
                const row = el.tagName === 'TR' ? el : el.closest('tr');
                if (!row) return;

                const nameCol = row.querySelector('.parameters-col_name');
                if (!nameCol) return;
                const colText = nameCol.textContent.trim().toLowerCase();
                if (!colText.includes('texts')) return;

                const input = row.querySelector('input[type="text"]');
                if (input && input.dataset.enhanced !== 'true') {
                    input.dataset.enhanced = 'true';
                    input.style.display = 'none';

                    const textarea = document.createElement('textarea');
                    textarea.className = 'json-schema-textarea';
                    textarea.name = 'texts';
                    textarea.rows = 10;
                    textarea.placeholder = defaultPlaceholder;
                    textarea.spellcheck = false;
                    textarea.value = input.value || '';

                    const syncValue = () => {
                        const val = textarea.value;
                        input.value = val;
                        const tracker = input._valueTracker;
                        if (tracker) tracker.setValue(val);
                        input.dispatchEvent(new Event('input', { bubbles: true }));
                        input.dispatchEvent(new Event('change', { bubbles: true }));
                    };

                    textarea.addEventListener('input', syncValue);
                    textarea.addEventListener('change', syncValue);

                    input.parentNode.insertBefore(textarea, input.nextSibling);
                }

                const ta = row.querySelector('textarea');
                if (ta) {
                    ta.classList.add('json-schema-textarea');
                    if (!ta.name) ta.name = 'texts';
                    ta.rows = 10;
                    if (!ta.placeholder) ta.placeholder = defaultPlaceholder;
                }
            });
        });
    }

    let scheduled = false;
    const observer = new MutationObserver(() => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => { scheduled = false; enhance(); });
    });
    window.addEventListener('pagehide', () => activeStates.forEach(release));
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            observer.observe(document.body, { childList: true, subtree: true });
            enhance();
        });
    } else {
        observer.observe(document.body, { childList: true, subtree: true });
        enhance();
    }
})();
