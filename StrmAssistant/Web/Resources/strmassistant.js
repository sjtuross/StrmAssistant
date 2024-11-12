define(['connectionManager', 'loading'], function (connectionManager, loading) {

    return {
        copy: function (libraryId) {

            loading.show();

            let apiClient = connectionManager.currentApiClient();
            let copyUrl = apiClient.getUrl('Library/VirtualFolders/Copy');

            apiClient.ajax({
                type: "POST",
                url: copyUrl,
                data: JSON.stringify({ Id: libraryId }),
                contentType: "application/json"
            }).finally(() => {
                loading.hide();
            });
        }
    };
});
