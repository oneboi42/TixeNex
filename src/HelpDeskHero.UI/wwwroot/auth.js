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