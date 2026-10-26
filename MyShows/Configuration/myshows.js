
const pluginId = 'ef35f6b1-7fe6-44ca-a215-232089fb9bc7'

function onViewShow() {
    const page = this
    page.querySelector('#create-account').innerHTML = 'Create an account at MyShows'
    loadConfig(page)
}

function loadConfig(page) {
    Dashboard.showLoadingMsg()

    return ApiClient.getPluginConfiguration(pluginId).then(config => {
        const userId = ApiClient.getCurrentUserId()
        const user = config.Users.find(e => e.Id == userId && e.AccessToken)
        const authError = page.querySelector('#auth-error')
        authError.innerText = user && user.AuthError
            ? 'MyShows rejected the saved credentials (' + user.AuthError + '). Sign in again to resume scrobbling.'
            : ''
        authError.style.display = authError.innerText ? 'block' : 'none'

        if (user && !user.AuthError) {
            page.querySelector('#ScrobbleAt').value = user.ScrobbleAt
            page.querySelector('#linked-account').innerText = user.Name
            showLogin(page, true)
        } else {
            showLogin(page, false)
        }
    }).catch(reason => {
        showLogin(page, false)
    }).finally(() => Dashboard.hideLoadingMsg())
}

function showLogin(page, isLoggedIn) {
    page.querySelector('#config-container').style.display = isLoggedIn ? 'block' : 'none'
    page.querySelector('#login-container').style.display = isLoggedIn ? 'none' : 'block'
}

function saveConfig(page, mutate) {
    return ApiClient.getPluginConfiguration(pluginId).then(config => {
        const userId = ApiClient.getCurrentUserId()
        if (mutate) {
            mutate(config, userId)
        } else {
            const user = config.Users.find(e => e.Id == userId)
            if (user)
                user.ScrobbleAt = page.querySelector("#config-container #ScrobbleAt").value
        }
        return ApiClient.updatePluginConfiguration(pluginId, config)
    }).then(Dashboard.processPluginConfigurationUpdateResult)
}

function onLoginClick(e) {
    const page = this.closest('.page')
    Dashboard.showLoadingMsg()

    const userId = ApiClient.getCurrentUserId()
    const apiParams = {
        id: userId,
        login: page.querySelector('#login').value,
        password: page.querySelector('#password').value
    }
    const request = {
        type: 'POST',
        url: ApiClient.getUrl('/MyShows/v2/login'),
        headers: { accept: "application/json" },
        data: apiParams
    }
    ApiClient.fetch(request).then((result) => {
        if (!result.success) {
            Dashboard.alert({
                message: 'An error occurred when trying to authorize device: ' + result.statusText
            })
        } else {
            loadConfig(page).then(() => saveConfig(page))
        }
    }).finally(() => Dashboard.hideLoadingMsg())

    return false
}

function onUnlinkClick() {
    const page = this.closest('.page')
    saveConfig(page, (config, userId) => {
        config.Users = config.Users.filter(e => e.Id != userId)
    }).then(() => loadConfig(page))
}

function onFormSubmit() {
    const page = this.closest('.page')
    saveConfig(page)
    return false
}

export default function (view, params) {
    view.addEventListener('viewshow', onViewShow)
    view.querySelector('#MyShowsConfigForm').addEventListener('submit', onFormSubmit)
    view.querySelector('#login-button').addEventListener('click', onLoginClick)
    view.querySelector('#unlink-button').addEventListener('click', onUnlinkClick)
};
