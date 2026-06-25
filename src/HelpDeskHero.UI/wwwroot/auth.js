window.authStorage = {
    getToken: function () {
        return localStorage.getItem('hdh_token');
    },
    setToken: function (value) {
        localStorage.setItem('hdh_token', value);
    },
    removeToken: function () {
        localStorage.removeItem('hdh_token');
    }
};

window.downloadFileFromBytes = function (fileName, contentType, bytes) {
    const blob = new Blob([new Uint8Array(bytes)], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = fileName || 'download';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    URL.revokeObjectURL(url);
};
