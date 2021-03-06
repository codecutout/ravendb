import deleteDocumentCommand = require("commands/deleteDocumentCommand");
import commandBase = require("commands/commandBase");
import database = require("models/database");
import shell = require("viewmodels/shell");
import getDocumentWithMetadataCommand = require("commands/getDocumentWithMetadataCommand");
import monitorRestoreCommand = require("commands/filesystem/monitorRestoreCommand");

class startRestoreCommand extends commandBase {
    private db: database = new database("<system>");

    constructor(private defrag: boolean, private restoreRequest: filesystemRestoreRequestDto, private updateRestoreStatus: (restoreStatusDto) => void) {
        super();
    }

    execute(): JQueryPromise<any> {
        var result = $.Deferred();

        new deleteDocumentCommand('Raven/FileSystem/Restore/Status/' + this.restoreRequest.FilesystemName, this.db)
            .execute()
            .fail((response: JQueryXHR) => {
                this.reportError("Failed to delete restore status document!", response.responseText, response.statusText);
                result.reject();
            })
            .done(_=> {
            this.post('/admin/fs/restore?defrag=' + this.defrag, ko.toJSON(this.restoreRequest), null, { dataType: 'text' })
                .fail((response: JQueryXHR) => {
                    this.reportError("Failed to restore backup!", response.responseText, response.statusText);
                    this.logError(response, result);
                })
                .done(() => new monitorRestoreCommand(result, this.restoreRequest.FilesystemName, this.updateRestoreStatus).execute());
            });

        return result;
    }

    private logError(response: JQueryXHR, result: JQueryDeferred<any>) {
        var r = JSON.parse(response.responseText);
        var restoreStatus: restoreStatusDto = { Messages: [r.Error], IsRunning: false };
        this.updateRestoreStatus(restoreStatus);
        result.reject();
    }
}

export = startRestoreCommand;