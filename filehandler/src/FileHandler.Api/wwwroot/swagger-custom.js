(function () {
    const defaultPlaceholder = '[\n  "Bản dịch 1",\n  "Bản dịch 2"\n]';

    // Helper to find the active textarea for translatedTexts
    function getExportTextarea() {
        return document.querySelector('.opblock-post[id*="operations-Files-export"] textarea, .opblock-post[id*="export"] textarea, textarea.json-schema-textarea, textarea[name="translatedTexts"]');
    }

    // 1. Intercept window.fetch: guarantee that multipart FormData contains the complete translatedTexts from textarea
    const originalFetch = window.fetch;
    window.fetch = function (input, init) {
        const url = typeof input === 'string' ? input : (input && input.url ? input.url : '');
        if (url.includes('/export')) {
            const body = init ? init.body : (input ? input.body : null);
            if (body instanceof FormData) {
                const existing = body.get('translatedTexts');
                if (!(existing instanceof File || existing instanceof Blob)) {
                    const ta = getExportTextarea();
                    if (ta && ta.value && ta.value.trim().length > 0) {
                        body.set('translatedTexts', ta.value.trim());
                    }
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
        if (body instanceof FormData && this._url && this._url.includes('/export')) {
            const existing = body.get('translatedTexts');
            if (!(existing instanceof File || existing instanceof Blob)) {
                const ta = getExportTextarea();
                if (ta && ta.value && ta.value.trim().length > 0) {
                    body.set('translatedTexts', ta.value.trim());
                }
            }
        }
        return originalXHRSend.apply(this, arguments);
    };

    // 3. DOM Enhancer: Upgrades single-line input to spacious textarea with monospace styling
    function enhance() {
        const exportBlock = document.querySelector('.opblock-post[id*="operations-Files-export"], .opblock-post[id*="export"]');
        if (!exportBlock) return;

        const rows = exportBlock.querySelectorAll('tr, .parameters-col_name');
        rows.forEach(el => {
            const row = el.tagName === 'TR' ? el : el.closest('tr');
            if (!row) return;

            const nameCol = row.querySelector('.parameters-col_name');
            if (!nameCol || !nameCol.textContent.includes('translatedTexts')) return;

            const input = row.querySelector('input[type="text"]');
            if (input && input.dataset.enhanced !== 'true') {
                input.dataset.enhanced = 'true';
                input.style.display = 'none';

                const textarea = document.createElement('textarea');
                textarea.className = 'json-schema-textarea';
                textarea.rows = 10;
                textarea.placeholder = defaultPlaceholder;
                textarea.spellcheck = false;
                textarea.value = input.value || '';

                textarea.addEventListener('input', () => {
                    const val = textarea.value;
                    input.value = val;
                    const tracker = input._valueTracker;
                    if (tracker) tracker.setValue(val);
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                });

                input.parentNode.insertBefore(textarea, input.nextSibling);
            }

            const ta = row.querySelector('textarea');
            if (ta) {
                ta.classList.add('json-schema-textarea');
                ta.rows = 10;
                if (!ta.placeholder) ta.placeholder = defaultPlaceholder;
            }
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
