define(['connectionManager', 'globalize', 'loading', 'toast', 'confirm'], function (connectionManager, globalize, loading, toast, confirm) {

    return {
        copy: function (libraryId) {

            loading.show();

            let apiClient = connectionManager.currentApiClient();
            let copyApi = apiClient.getUrl('Library/VirtualFolders/Copy');

            apiClient.ajax({
                type: "POST",
                url: copyApi,
                data: JSON.stringify({ Id: libraryId }),
                contentType: "application/json"
            }).finally(() => {
                loading.hide();
                const locale = globalize.getCurrentLocale().toLowerCase();
                const confirmMessage = (locale === 'zh-cn') ? '\u590d\u5236\u5a92\u4f53\u5e93\u6210\u529f' : 
                    (['zh-hk', 'zh-tw'].includes(locale) ? '\u8907\u88fd\u5a92\u9ad4\u5eab\u6210\u529f' : 'Copy Library Success');
                toast(confirmMessage);
                const itemsContainer = document.querySelector('.view .itemsContainer');
                if (itemsContainer) {
                    itemsContainer.notifyRefreshNeeded(true);
                }
            });
        },

        remove: function (libraryId, libraryName) {
            confirm({
                text: globalize.translate('MessageAreYouSureYouWishToRemoveLibrary').replace('{0}', libraryName),
                title: globalize.translate('HeaderRemoveLibrary'),
                confirmText: globalize.translate('Remove'),
                primary: 'cancel'
            })
            .then(function() {
                loading.show();

                let apiClient = connectionManager.currentApiClient();
                let deleteApi = apiClient.getUrl('Library/VirtualFolders/Delete');

                apiClient.ajax({
                    type: "POST",
                    url: deleteApi + "?refreshLibrary=false&id=" + libraryId,
                    data: {},
                    contentType: "application/json"
                }).finally(() => {
                    loading.hide();
                    const locale = globalize.getCurrentLocale().toLowerCase();
                    const confirmMessage = (locale === 'zh-cn') ? '\u5408\u96c6\u5220\u9664\u6210\u529f' : 
                        (['zh-hk', 'zh-tw'].includes(locale) ? '\u5408\u96C6\u5236\u9662\u6210\u529F' : 'Delete Collections Success');
                    toast(confirmMessage);
                    const itemsContainer = document.querySelector('.view .itemsContainer');
                    if (itemsContainer) {
                        itemsContainer.notifyRefreshNeeded(true);
                    }
                });
            });
        }
    };
});
