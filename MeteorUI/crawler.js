function disableRemove(collection)
{
  collection.deny({
    remove: function(userId, document) {
      return true;
    }
  });
}

States = new Mongo.Collection("states");
Sites = new Mongo.Collection("sites");
Queries = new Mongo.Collection("queries");
//disableRemove(Queries);

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
  var edge = Npm.require('edge');
  var path = Npm.require('path');

  var base = path.resolve('../../../../../.');

  var createCrawlerProxy = function() {
    var CrawlerProxy = edge.func({
      assemblyFile: path.resolve(base, '../Platform.Web.Crawler/bin/Debug/Platform.Web.Crawler.dll'),
      typeName: 'Platform.Web.Crawler.EdgeJsProxy',
    });

    return CrawlerProxy(path.join(base, 'crawler.links'), true);
  }
  
  var crawlerProxy = createCrawlerProxy();
  
  var crawlerState = States.findOne();
  
  if(!crawlerState)
  {
    States.insert({ value: "stopped" });
    crawlerState = States.findOne();
  }

  Meteor.methods({
    startCrawl: function () {
        var sitesToCrawl = Sites.find({ crawEnabled: true }).fetch();
        
        var urlsToCrawl = [];
        
        for(var i = 0; i < sitesToCrawl.length; i++)
          urlsToCrawl.push(sitesToCrawl[i].url);
        
        crawlerProxy.StartCrawl({ urls: urlsToCrawl, pageCrawled: Meteor.bindEnvironment(function (result) {
          console.dir(result.SiteUrl);
          Sites.update({ url: result.SiteUrl }, {
            $inc: { crawledPages: 1 }
          });
        })}, Meteor.bindEnvironment(function () {
          States.update(crawlerState._id, {
            $set: {
              value: "stopped",
              switchInProgress: false
            }
          });
        }));
    },
    stopCrawl: function () {
      crawlerProxy.StopCrawl({}, Meteor.bindEnvironment(function () {
        States.update(crawlerState._id, {
          $set: {
            switchInProgress: false
          }
        });
      }));
    },
    shutdown: function () {
      crawlerProxy.Dispose({}, true);
    },
    reset: function () {
      crawlerProxy.Reset({}, true);
      
      crawlerProxy = createCrawlerProxy();
    
      Sites.remove({});
      Queries.remove({});
      Results.remove({});
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
    type: String,
    defaultValue: ""
  }
}));

Results = new Mongo.Collection("results");

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

// Router.route('/', function() {
//   this.render('home');
// });

// Router.route('/', function() {
//   this.render('sites');
// });

// Router.route('/', function() {
//   this.render('queries');
// });

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
      return {
        query: Queries.findOne({ _id: this.params._id }),
        queryId: this.params._id,
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
  
  Template.dataCollectionSwitch.helpers({
    switchInProgress: function() {
      return this.switchInProgress;
    },
    running: function() {
      return this.value == "running";
    },
    stopped: function() {
      return this.value == "stopped";
    },
    switchCompleted: function() {
      return !this.switchInProgress;
    },
  });
  
  Template.dataCollectionSwitch.events({
    'click a[name=start]': function(event, template) {
      States.update(template.data._id, {
        $set: {
          value: "running",
          switchInProgress: true
        }
      }, function() {
        Meteor.call('startCrawl', function() {
          States.update(template.data._id, {
            $set: {
              switchInProgress: false
            }
          });
        });
      });
    },
    'click a[name=stop]': function(event, template) {
      States.update(template.data._id, {
        $set: {
          value: "stopped",
          switchInProgress: true
        }
      }, function() {
        Meteor.call('stopCrawl', function() { });
      });
    },
    'click a[name=reset]': function(event, template) {
      States.update(template.data._id, {
        $set: {
          value: "stopped",
          switchInProgress: true
        }
      }, function() {
        
        Meteor.call('reset', function() {
          States.update(template.data._id, {
            $set: {
              switchInProgress: false
            }
          });
        });
        
      });
    }
  });
  
  Template.sites.helpers({
    sites: function() {
      return Sites.find();
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
}

// Router.route('/query/:q', function() {

//   var queryDocument = Queries.findOne({
//     query: this.params.q
//   });

//   if (!queryDocument) {
//     Queries.insert({
//       query: this.params.q
//     });
//   }

//   console.dir(queryDocument);

//   this.render('query', {
//     data: {
//       Query: queryDocument
//     }
//   });
// });

if (Meteor.isClient) {
  // AutoForm.hooks({
  //   insertQueries: {
  //     onSubmit: function (insertDoc, updateDoc, currentDoc) {
  //       console.dir(insertDoc);
  //       console.dir(updateDoc);
  //       console.dir(currentDoc);
  //       return false;
  //     }
  //   }
  // });
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
  // Template.navbar.events({
  //   'click button[name=search]': function(event, template) {
  //     //Router.go('/query/' + );
  //   }
  // });
}