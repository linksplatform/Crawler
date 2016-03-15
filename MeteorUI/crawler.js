if(Meteor.isServer) {
  (function() {
    
    var handlers = [];
    
    Meteor.shutdown = function(callback) {
      if(callback && callback instanceof Function) {
        handlers.push(callback);
      } else {
        if (!Meteor.shutdowned) {
          Meteor.shutdowned = true;
        
          for(var i = 0; i < handlers.length; i++)
            handlers[i]();
          
          process.exit(); // works only with "meteor --once" (if called directly from user's code)
        }
      }
    };
    
    // From https://github.com/numtel/meteor-mysql/blob/8825011259ab10772154c7e869bb220e69e48770/README.md#closing-connections-between-hot-code-pushes
    // Close connections on hot code push
    process.on('SIGTERM', Meteor.shutdown);
    // Close connections on exit (ctrl + c)
    process.on('SIGINT', Meteor.shutdown);
    
    // See also https://github.com/meteor/meteor/blob/1bd0d4764a9ed8909d44b02871b55a0d440ace57/tools/cleanup.js
    
    // Adding this somehow works better (or no?)
    process.on('SIGHUP', Meteor.shutdown);
    process.on('exit', Meteor.shutdown);
    
  })();
}

States = new Mongo.Collection("states");
Sites = new Mongo.Collection("sites");
Queries = new Mongo.Collection("queries");
Results = new Mongo.Collection("results");

States.attachSchema(new SimpleSchema({
  value: {
    type: String,
    defaultValue: "stopped"
  },
  switchInProgress: {
    type: Boolean,
    optional: true,
    defaultValue: false
  }
}))

if(Meteor.isServer)
{
  Sites.deny({
    insert: function(userId, document) {
      var url = document.url;

      if (url[url.length - 1] != '/') url += '/';
      
      return Sites.findOne({ url: url }) != null;
    }
  });

  Queries.deny({
    insert: function(userId, document) {
      var state = States.findOne();
      if (state.switchInProgress || state.value == 'shutdown' || state.value == 'crawling')
        return true;
      
      return Queries.findOne({ value: document.value }) != null;
    }
  });

  Queries.after.insert(function (userId, document) {
    Meteor.call('startSearch', document, function() { });
  });
}

if(Meteor.isServer)
{
  //var edge = Npm.require('edge');
  var edge = Meteor.npmRequire('edge');
  var path = Npm.require('path');

  var base = path.resolve('../../../../../.');

  var createCrawlerProxy = function() {
    var CrawlerProxy = edge.func({
      assemblyFile: path.resolve(base, '../Platform.Web.Crawler/bin/Debug/Platform.Web.Crawler.dll'),
      typeName: 'Platform.Web.Crawler.EdgeJsProxy',
    });

    var proxyObject = CrawlerProxy({
      dataPath: path.join(base, 'crawler.links'),
      logPath: path.join(base, 'log.txt')
    }, true);
    
    Meteor.shutdown(function() {
      if(!proxyObject.disposed) { // Used to guard from multiple executions.
        proxyObject.disposed = true;
        proxyObject.Dispose({}, true);
      }
    });
    
    return proxyObject;
  }
  
  var crawlerProxy = createCrawlerProxy();
  
  var crawlerState = States.findOne();
  
  if(!crawlerState)
  {
    States.insert({ value: "stopped" });
    crawlerState = States.findOne();
  }
  else
  {
    States.update(crawlerState._id, {
      $set: {
        value: "stopped",
        switchInProgress: false
      }
    });
    crawlerState = States.findOne();
  }

  Meteor.methods({
    startCrawl: function () {
      var sitesToCrawl = Sites.find({ crawEnabled: true }).fetch();
      
      var urlsToCrawl = [];
      
      for(var i = 0; i < sitesToCrawl.length; i++)
        urlsToCrawl.push(sitesToCrawl[i].url);
      
      var queriesToCrawl = Queries.find({ }).fetch();
      
      crawlerProxy.StartCrawl({ urls: urlsToCrawl, pageCrawled: Meteor.bindEnvironment(function (result, callback) {
        Sites.update({ url: result.SiteUrl }, {
          $inc: { crawledPages: 1 }
        }, function () { });
        
        // May be slow
        for(var i = 0; i < queriesToCrawl.length; i++)
        {
          if (result.PageContent.indexOf(queriesToCrawl[i].value) >= 0)
          {
            Queries.update(queriesToCrawl[i]._id, {
              $inc: { results: 1 },
              $set: { finished: new Date() }
            }, function () { });
            
            Results.insert({ query: queriesToCrawl[i]._id, url: result.PageUrl }, function () { });
          }
        }
        
        callback(null, true);
      })}, Meteor.bindEnvironment(function () {
        // Crawl finished
        States.update({ value: "crawling" }, {
          $set: {
            value: "stopped",
            switchInProgress: false
          }
        });
      }));
      
      return true;
    },
    stopCrawl: function () {
      crawlerProxy.StopCrawl({}, Meteor.bindEnvironment(function (result) {
        States.update(crawlerState._id, { $set: { switchInProgress: false } });
      }));
      
      return true;
    },
    startSearch: function (query) {
      crawlerProxy.StartSearch({
        query: query.value, 
        pageFound: Meteor.bindEnvironment(function (result, callback) {
          Queries.update(query._id, {
            $inc: { results: 1 }
          }, function() { });
          Results.insert({ query: query._id, url: result.Url }, function () { });
          
          callback(null, true);
        }),
        searchFinished: Meteor.bindEnvironment(function (result, callback) {
          Queries.update(query._id, {
            $set: { finished: new Date() }
          }, function () { });
          
          callback(null, true);
        })
      }, function() { });
    },
    stopSearch: function () {
      crawlerProxy.StopSearch({}, Meteor.bindEnvironment(function (result) {
        States.update(crawlerState._id, { $set: { switchInProgress: false } });
      }));
      
      return true;
    },
    restart: function () {
      crawlerProxy = createCrawlerProxy();
      
      return true;
    },
    shutdown: function () {
      crawlerProxy.Dispose({}, Meteor.bindEnvironment(function (result) {
        crawlerProxy.disposed = true; // Prevents running Dispose on auto clean up.
        crawlerProxy = null;
        
        States.update(crawlerState._id, { $set: { switchInProgress: false } });
      }));
      
      return true;
    },
    reset: function () {
      crawlerProxy.Reset({}, Meteor.bindEnvironment(function (result) {
        crawlerProxy = createCrawlerProxy();
    
        Sites.remove({});
        Queries.remove({});
        Results.remove({});
        
        States.update(crawlerState._id, { $set: { switchInProgress: false } });
      }));
      
      return true;
    },
  });
}

Sites.attachSchema(new SimpleSchema({
  url: {
    type: String,
    regEx: SimpleSchema.RegEx.Url,
    autoValue: function() {
      if(this.isSet) {
        if (this.value[this.value.length - 1] != '/') return this.value + '/';
      }
    }
  },
  crawledPages: {
    type: Number,
    optional: true,
    defaultValue: 0
  },
  crawEnabled: {
    type: Boolean,
    optional: true,
    defaultValue: true
  }
}));

Queries.attachSchema(new SimpleSchema({
  value: {
    type: String
  },
  results: {
    type: Number,
    optional: true,
    defaultValue: 0
  },
  finished: {
    type: Date,
    optional: true
  }
}));

Results.attachSchema(new SimpleSchema({
  query: {
    type: String
  },
  url: {
    type: String,
    regEx: SimpleSchema.RegEx.Url
  },
  fragment: {
    type: String,
    optional: true
  }
}));

Router.configure({
  layoutTemplate: 'layout'
});

Router.map(function () {
  this.route('home', {
    path: '/',
    template: 'home'
  });
  this.route('sites', {
    path: '/sites',
    template: 'sites'
  });
  this.route('queries', {
    path: '/queries',
    template: 'queries'
  });
  this.route('query', {
    path: '/query/:_id',
    data: function () {
      var q = Queries.findOne({ _id: this.params._id });
      return {
        query: q,
        queryId: this.params._id,
        queryValue: q.value,
      }
    },
    template: 'queryResults'
  });
});

if (Meteor.isClient) {
  Template.registerHelper('Sites', function() { return Sites; });
  Template.registerHelper('Queries', function() { return Queries; });
  Template.registerHelper('Results', function() { return Results; });
  
  Template.leftNavItems.helpers({
    activeIfTemplateIs: function (template) {
      var currentRoute = Router.current();
      return currentRoute &&
        template === currentRoute.lookupTemplate() ? 'active' : '';
    }
  });
  
  Template.rightNavItems.helpers({
    states: function() {
      return States.find();
    }
  });
  
  Template.applicationSwitch.helpers({
    switchInProgress: function() {
      return this.switchInProgress;
    },
    switchCompleted: function() {
      return !this.switchInProgress;
    },
    crawling: function() {
      return this.value == "crawling";
    },
    stopped: function() {
      return this.value == "stopped";
    },
    running: function() {
      return this.value != "shutdown";
    },
    shutdown: function() {
      return this.value == "shutdown";
    }
  });
  
  function switchTo(state, call, template)
  {
    States.update(template.data._id, {
      $set: {
        value: state,
        switchInProgress: true
      }
    }, function() {
      Meteor.call(call, function() {
        States.update(template.data._id, { $set: { switchInProgress: false } });
      });
    });
  }
  
  function switchToNoReset(state, call, template)
  {
    States.update(template.data._id, {
      $set: {
        value: state,
        switchInProgress: true
      }
    }, function() {
      Meteor.call(call);
    });
  }
  
  Template.applicationSwitch.events({
    'click a[name=turn-on]': function(event, template) {
      switchTo('stopped', 'restart', template);
    },
    'click a[name=turn-off]': function(event, template) {
      switchToNoReset('shutdown', 'shutdown', template);
    },
    'click a[name=start-crawl]': function(event, template) {
      switchTo('crawling', 'startCrawl', template);
    },
    'click a[name=stop]': function(event, template) {
      switchToNoReset('stopped', 'stopCrawl', template);
    },
    'click a[name=reset]': function(event, template) {
      switchToNoReset('stopped', 'reset', template);
    }
  });
  
  Template.sites.helpers({
    sites: function() {
      return Sites.find();
    }
  });
  
  Template.query.helpers({
    searching: function() {
      return this.finished == null;
    },
    finishedFormatted: function() {
      return moment(this.finished).format('LLL');
    }
  });
  
  Template.queries.helpers({
    queries: function() {
      return Queries.find();
    }
  });
  
  Template.queryResults.helpers({
    results: function() {
      return Results.find({ query: this.queryId });
    }
  });

  Template.site.events({
    'click button[name=false]': function(event, template) {
      Sites.update(template.data._id, {
        $set: {
          crawEnabled: false
        }
      });
    },
    'click button[name=true]': function(event, template) {
      Sites.update(template.data._id, {
        $set: {
          crawEnabled: true
        }
      });
    }
  });
}