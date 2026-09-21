importScripts('swagger-multipart.js', 'swagger-response.js');

self.onmessage = function (event) {
    const { buffer, contentType } = event.data;
    try {
        const result = self.FileHandlerResponse.prepare(new Uint8Array(buffer), contentType);
        // Parts are views into one transferred buffer; binary files are never decoded as text.
        self.postMessage({ result }, [buffer]);
    } catch (error) {
        self.postMessage({ error: error.message });
    }
};
