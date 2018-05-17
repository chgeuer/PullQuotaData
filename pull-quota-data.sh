#!/bin/bash

subscriptions=$(az account list --query "[].id" -o tsv)
# subscriptions="724467b5-bee4-484b-bf13-d6a5505d2b51"
for subscription in ${subscriptions} 
do 
    az account set --subscription "${subscription}"
    locations=$(az account list-locations --query "[].name" -o tsv)
    # locations="westeurope"
    for location in ${locations} 
    do
        ncls=$(az vm list-usage --location "${location}" --query "[].[name.value, currentValue, limit]" -o tsv)
        while read -r ncl; do
            echo "${subscription}	${location}	compute	$ncl"
        done <<< "$ncls"

        ncls=$(az network list-usages --location "${location}" --query "[].[name.value, currentValue, limit]" -o tsv)
        while read -r ncl; do
            echo "${subscription}	${location}	network	$ncl"
        done <<< "$ncls"
    done
done
