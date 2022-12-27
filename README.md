# Specify!


# What is it good for

***Absolutely(Almost) Everything***

**Specify** is one part of the two part stack of **Specify** and **Specified**.

**Specify** collects data from the system it's running on and then passes all this data to a website;
www.spec-ify.com, henceforth **Specified**.


## Methodology

**Specify** probes various WMI and win32 classes on the host machine for an exhaustive list of data, parsing and pushing keyed information into a JSON standard object tree.


## What kinds of data?

**Specify** is developed with the intent of being used as a quick way to relay the state of one's system to another person, as such, the data gathered is always whatever is relevant for the diagnosis of a tech issue.

There is no explicitly **sensitive** or **private** information that gets passed in.
This does not necessarily mean that there is no **subjectively sensitive** or **private information** that does get included into the snapshot that **Specify** generates.

There is a good-will effort to give the user the option to omit certain pieces of information that can be most sensitive, the username of the host machine, and the path for **commercial** OneDrive instances, which by nature include **places of work** or **education** in their path.

## Data Retention

The snapshot generated by **Specify** is as previously mentioned, a JSON object tree that is passed to **Specified** via an internal POST request. 
This snapshot lives as a .json file in the webserver's file architecture so **Specified** can ingest and format the information into the desired view.

This JSON file has an expiration date of 24 hours, after which it is deleted automatically.
This does mean that your snapshot, should you want to revisit it, will potentially have to be re-ran.

No information is otherwise kept in storage, and no **cookies** are set by **Specified** of any form.


## Fallback

As previously mentioned, **Specify** automatically **POST**s the results of it's runtime to **Specified** and then automatically opens the customized page, while also copying that URL to the clipboard.

Alternatively, whenever **Specify** is unable to complete said **POST** request, the **.json** file is instead written into the same directory as the **Specify** **executable** is ran from.
This **.json** can then be manually uploaded to **Specified** to generate the view.
