#!/bin/bash

repomix --quiet --config .project/repomix/repomix.config.balanced.json
repomix --quiet --config .project/repomix/repomix.config.minimal.json
repomix --top-files-len 10 --token-count-tree 5000 --config .project/repomix/repomix.config.full.json
