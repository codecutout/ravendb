/// <reference path="promise.ts" />
/// <reference path="../../Scripts/typings/knockout/knockout.d.ts" />
/// <reference path="../../Scripts/typings/jquery/jquery.d.ts" /> 


import database = module("models/database");
import collection = module("models/collection"); 
import collectionInfo = module("models/collectionInfo");
import document = module("models/document");
import pagedResultSet = module("common/pagedResultSet");

class raven {

    private baseUrl = "http://localhost:8080"; // For debugging purposes, uncomment this line to point Raven at an already-running Raven server. Requires the Raven server to have it's config set to <add key="Raven/AccessControlAllowOrigin" value="*" />
	//private baseUrl = ""; // This should be used when serving HTML5 Studio from the server app.
	
	public static activeDatabase = ko.observable<database>().subscribeTo("ActivateDatabase");

    public databases(): promise<database[]> {
		var resultsSelector = (databaseNames: string[]) => databaseNames.map(n => new database(n));
		return this.fetch("/databases", { pageSize: 1024 }, null, resultsSelector);		
    }

	public collections(): promise<collection[]> {
		this.requireActiveDatabase();

        var args = {
            field: "Tag",
            fromValue: "",
            pageSize: 100
		};
		var resultsSelector = (collectionNames: string[]) => collectionNames.map(n => new collection(n));
        return this.fetch("/terms/Raven/DocumentsByEntityName", args, raven.activeDatabase(), resultsSelector);
	}

	public collectionInfo(collectionName?: string, documentsSkip = 0, documentsTake = 0): promise<collectionInfo> {
		this.requireActiveDatabase();

		var args = {
			query: collectionName ? "Tag:" + collectionName : undefined,
			start: documentsSkip,
			pageSize: documentsTake
		};

		var resultsSelector = (dto: collectionInfoDto) => new collectionInfo(dto);
		var url = "/indexes/Raven/DocumentsByEntityName";
		return this.fetch(url, args, raven.activeDatabase(), resultsSelector);
	}

	// should return promise<pagedResultSet>, but TS compiler doesn't like this.
	public documents(collectionName: string, skip = 0, take = 30): promise<any> {
		this.requireActiveDatabase();

		var documentsTask = $.Deferred();
		this.collectionInfo(collectionName, skip, take)
			.then(collection => {
				var items = collection.results; 
				var resultSet = new pagedResultSet(items, collection.totalResults);
				documentsTask.resolve(resultSet);
			});
		return documentsTask;
    }

    public document(id: string): promise<document> {
        var resultsSelector = (dto: documentDto) => new document(dto);
        var url = "/docs/" + encodeURIComponent(id);
        return this.fetch(url, null, raven.activeDatabase(), resultsSelector);
    }

    public documentWithMetadata(id: string): promise<document> {
        var resultsSelector = (dtoResults: documentDto[]) => new document(dtoResults[0]);
        var url = "/docs/";
        var args = {
            startsWith: id,
            start: 0, 
            pageSize: 1
        };
        return this.fetch(url, args, raven.activeDatabase(), resultsSelector);
    }

    public deleteDocuments(ids: string[]): promise {
        this.requireActiveDatabase();

        var deleteDocs = ids.map(id => this.createDeleteDocument(id));
        //var deleteHeaders = headers: { 'x-my-custom-header': 'some value' }
        return this.post("/bulk_docs", ko.toJSON(deleteDocs), raven.activeDatabase());
    }

    private createDeleteDocument(id: string) {
        return {
            Key: id,
            Method: "DELETE",
            Etag: null,
            AdditionalData: null
        }
    }    

	private requireActiveDatabase(): void {
		if (!raven.activeDatabase()) {
			throw new Error("Must have an active database before calling this method.");
		}
	}

    private fetch(relativeUrl: string, args: any, database?: database, resultsSelector?: (results: any) => any): JQueryPromise {
        var ajax = this.ajax(relativeUrl, args, "GET", database);

		var foo: JQueryXHR = null;
		if (resultsSelector) {
			var task = $.Deferred();
			ajax.done((results) => {
				var transformedResults = resultsSelector(results);
				task.resolve(transformedResults);
            });
            ajax.fail((request, status, error) => task.reject(request, status, error));
			return task;
		} else {
			return ajax; 
		}
    }

    private post(relativeUrl: string, args: any, database?: database, customHeaders?: any): JQueryPromise {
        return this.ajax(relativeUrl, args, "POST", database, customHeaders);
    }

    private ajax(relativeUrl: string, args: any, method: string, database?: database, customHeaders?: any): JQueryPromise {
        
        var options = {
            cache: false,
            url: this.baseUrl + (database && database.isSystem === false ? "/databases/" + database.name : "") + relativeUrl,
            data: args,
            type: method
        };

        if (customHeaders) {
            for (var prop in customHeaders) {
                options[prop] = customHeaders[prop];
            }
        }
        
        return $.ajax(options);
    }
}

export = raven;