import commandBase = require("commands/commandBase");
import database = require("models/database");

class getDatabasesCommand extends commandBase {
    
    execute(): JQueryPromise<database[]> {
        var args = {
            pageSize: 1024,
            getAdditionalData: true
        };

        var url = "/databases";

        var resultsSelector = (databases: databaseDto[]) => databases.map((db: databaseDto) => new database(db.Name, db.Disabled, db.Bundles, db.IndexingDisabled));
        return this.query(url, args, null, resultsSelector);
    }
}

export = getDatabasesCommand;