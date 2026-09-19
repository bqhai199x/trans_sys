(function () {
    const defaultPlaceholder = '[\n  "Bản dịch 1",\n  "Bản dịch 2"\n]';

    // Helper to find the active export opblock for a given URL
    function findExportBlock(url) {
        const urlLower = (url || '').toLowerCase();
        const blocks = document.querySelectorAll('.opblock-post');
        for (const block of blocks) {
            const pathEl = block.querySelector('.opblock-summary-path');
            const path = pathEl ? pathEl.textContent.trim().toLowerCase() : '';
            if (path && (urlLower.endsWith(path) || urlLower.includes(path))) {
                return block;
            }
        }
        return document.querySelector('.opblock-post.is-open[id*="export" i], .opblock-post[id*="export" i]');
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
        return originalFetch.apply(this, arguments);
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
        return originalXHRSend.apply(this, arguments);
    };

    // 3. DOM Enhancer: Upgrades single-line input to spacious textarea with monospace styling across all export endpoints
    function enhance() {
        const exportBlocks = document.querySelectorAll('.opblock-post[id*="export" i], .opblock-post');
        exportBlocks.forEach(block => {
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

    const observer = new MutationObserver(() => enhance());
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
