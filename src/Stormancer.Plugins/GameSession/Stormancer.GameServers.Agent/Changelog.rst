=========
Changelog
=========

All notable changes to this project will be documented in this file.

The format is based on `Keep a Changelog <https://keepachangelog.com/en/1.0.0/>`_, except reStructuredText is used instead of Markdown.
Please use only reStructuredText in this file, no Markdown!

This project adheres to semantic versioning.


Unreleased
----------
Added
*****
- Create server crash reports and store them locally.
- Added APIs and systems to support best region detection.
- Added web API to 

0.3.0.8
-------
Changed
*******
- Add support for multiple apps to agents.
- GetStatus now reports the reserved resources on the node.
- Support more than 8Gb of total reservable memory.
- Add code to also reconnect to the serverpools scene on server reconnect.

Fixed
*****
- List containers now returns properly living containers managed by the agent.

0.2.0
-----
Added
*****
- Initial preversion of the agent.