function disableRemove(collection)
{
  collection.deny({
    remove: function(userId, document) {
      return true;
    }
  });
}

Meteor.methods({
  reset: function () {
      Sites.remove({});
      Queries.remove({});
      Results.remove({});
  },
});

States = new Mongo.Collection("states");

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
  if(!States.findOne())
    States.insert({ value: "stopped" });
}

Sites = new Mongo.Collection("sites");
//disableRemove(Sites);

Sites.attachSchema(new SimpleSchema({
  url: {
    type: String,
    regEx: SimpleSchema.RegEx.Url
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

Queries = new Mongo.Collection("queries");
//disableRemove(Queries);

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
        setTimeout(function(){
          States.update(template.data._id, {
            $set: {
              switchInProgress: false
            }
          });
        }, 500);
      });
    },
    'click a[name=stop]': function(event, template) {
      States.update(template.data._id, {
        $set: {
          value: "stopped",
          switchInProgress: true
        }
      }, function() {
        
        setTimeout(function(){
          States.update(template.data._id, {
            $set: {
              switchInProgress: false
            }
          });
        }, 500);
        
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